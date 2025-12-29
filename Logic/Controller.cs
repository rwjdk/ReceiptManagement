using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using Azure;
using JetBrains.Annotations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

#pragma warning disable RAG003

namespace Logic;

public class Controller(
    AzureOpenAIAgentFactory agentFactory,
    AzureOpenAIEmbeddingFactory embeddingFactory,
    FinancialRecordQuery financialRecordQuery,
    FinancialRecordCommand financialRecordCommand,
    BankFileQuery bankFileQuery)
{
    public async Task<List<FinancialRecord>> AddNewEntries(Action<string> notifyProgress, List<MatchRule> matchRules, string newDateRageCsv, string pathToUnprocessedPdfs, string rootDataFolder, int year, string account)
    {
        int step = 0;
        int totalSteps = 5;

        step++;
        notifyProgress.Invoke($"{step}/{totalSteps}: Reading new Records");
        BankEntry[] entries = bankFileQuery.ReadEntries(newDateRageCsv);

        AzureOpenAIAgent yieldCompanyAgent = agentFactory.CreateAgent(new AgentOptions
        {
            Model = OpenAIChatModels.Gpt52,
            Instructions = "You are a Stock Expert",
            ReasoningEffort = OpenAIReasoningEffort.Minimal
        });

        AzureOpenAIAgent invoiceDetailsAgent = agentFactory.CreateAgent(new AgentOptions
        {
            Model = OpenAIChatModels.Gpt5Mini,
            Instructions = "You are an Expert in analyzing raw PDF Data and extracting what was bought",
            ReasoningEffort = OpenAIReasoningEffort.Minimal
        });

        SqliteVectorStore vectorStore = new SqliteVectorStore("Data Source=" + rootDataFolder + "\\vector-store.db", new SqliteVectorStoreOptions
        {
            EmbeddingGenerator = embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small")
        });

        SqliteCollection<string, VectorStoreRecord> collection = vectorStore.GetCollection<string, VectorStoreRecord>("Data");
        await collection.EnsureCollectionExistsAsync();

        List<VectorStoreRecord> existingRecords = [];
        await foreach (VectorStoreRecord storeRecord in collection.GetAsync(filter: record => record.Id != "", top: int.MaxValue))
        {
            existingRecords.Add(storeRecord);
        }

        string[] existingIds = existingRecords.Select(x => x.Id).ToArray();
        string[] unprocessedPdfs = Directory.GetFiles(pathToUnprocessedPdfs, "*.pdf", SearchOption.AllDirectories);
        int unprocessedFileCount = unprocessedPdfs.Length;
        step++;
        int counter = 0;
        List<string> activeIds = [];
        foreach (string pdfPath in unprocessedPdfs)
        {
            activeIds.Add(pdfPath);
            counter++;
            if (existingIds.Contains(pdfPath))
            {
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(pdfPath);
            PdfDocument document = PdfDocument.Open(bytes);
            string pdfText = string.Empty;
            foreach (Page page in document.GetPages())
            {
                IEnumerable<IPdfImage> images = page.GetImages();
                foreach (IPdfImage image in images)
                {
                    Console.WriteLine(""); //Todo - extract text from image
                }

                pdfText += page.Text + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(pdfText))
            {
                continue;
            }

            try
            {
                string instructions = $"""
                                       Clean up the following PDF text '{pdfText}' 
                                       so 
                                       - Company that issued of the Invoice
                                       - Product(s)
                                       - Dates
                                       - Amounts and Currency
                                       are left. 

                                       Rules:
                                       - Company that issued of the Invoice is never 'RWJ Invest'.
                                       - Remove customer date (RWJ Invest / Rasmus Wulff Jensen)
                                       - Remove their Address (Chr. Winthers vej 83,st,tv 8230 Åbyhøj)
                                       """;
                ChatClientAgentRunResponse<PdfCleanUp> response = await invoiceDetailsAgent.RunAsync<PdfCleanUp>(instructions);

                notifyProgress.Invoke($"{step}/{totalSteps}: Ingesting {unprocessedFileCount} unprocessed PDFs into a vector-store ({counter}/{unprocessedFileCount})");

                PdfCleanUp result = response.Result;
                await collection.UpsertAsync(new VectorStoreRecord
                {
                    Id = pdfPath,
                    Content = result.ToString(pdfText),
                    Amount = Convert.ToInt32(result.Amount * 100),
                    Date = result.Date?.ToString("yyyyMMdd"),
                    Month = result.Month,
                    Issuer = result.Issuer,
                    FileName = Path.GetFileName(pdfPath)
                });
            }
            catch (Exception e)
            {
                notifyProgress.Invoke($"{step}/{totalSteps}: Ingesting {unprocessedFileCount} unprocessed PDFs into a vector-store ({counter}/{unprocessedFileCount})");
                counter++;
                await collection.UpsertAsync(new VectorStoreRecord
                {
                    Id = pdfPath,
                    Content = pdfText,
                    FileName = Path.GetFileName(pdfPath)
                });
            }
        }

        IEnumerable<string> deadIds = existingIds.Except(activeIds);
        await collection.DeleteAsync(deadIds);

        step++;
        notifyProgress.Invoke($"{step}/{totalSteps}: Reading existing Records");
        List<FinancialRecord> existing = financialRecordQuery.GetExisting(rootDataFolder, year, account);
        FinancialRecord[] fromBankEntries = await financialRecordQuery.FromBankEntries(entries, matchRules.ToArray());

        step++;
        notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records");
        foreach (FinancialRecord newEntry in fromBankEntries.Reverse())
        {
            if (existing.Any(x => x.BankEntry == newEntry.BankEntry))
            {
                continue;
            }

            if (newEntry.MatchResults.Length == 1)
            {
                //Good match
                MatchResult matchResult = newEntry.MatchResults[0];
                if (false && matchResult.NeedDividedCompanyMatch) //todo - add back in
                {
                    notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records (Finding Divided Company from {newEntry.BankEntry.Text})");
                    string description = newEntry.Description ?? string.Empty;
                    ChatClientAgentRunResponse<DividendCompanyResult> response = await yieldCompanyAgent.RunAsync<DividendCompanyResult>("What company does this refer to?: " + newEntry.BankEntry.Text);
                    string yieldCompany = response.Result.CompanyName;
                    description = description.Replace("<COMPANY>", yieldCompany);
                    newEntry.Description = description;
                }

                if (matchResult.NeedAttachment)
                {
                    List<VectorStoreRecord> vectorStoreSearchResult = [];
                    notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records (Finding Related PDF to {newEntry.BankEntry.Text})");
                    string query = newEntry.ToString();
                    await foreach (VectorSearchResult<VectorStoreRecord> result in collection.SearchAsync(query, 12, new VectorSearchOptions<VectorStoreRecord>
                                   {
                                       IncludeVectors = false
                                   }))
                    {
                        vectorStoreSearchResult.Add(result.Record);
                    }

                    StringBuilder searchResult = new();
                    foreach (VectorStoreRecord record in vectorStoreSearchResult)
                    {
                        searchResult.AppendLine(record.ToString());
                    }

                    string whatFileOfThese = "What File of these: " + searchResult + $" is the best match for this record: {newEntry} (Issuer, Amount (Might be different currency so adjust) and Month/Approximate Date is the best match-conditions)";
                    ChatClientAgentRunResponse<DocumentMatch> responseDocumentMatch = await invoiceDetailsAgent.RunAsync<DocumentMatch>(whatFileOfThese);
                    VectorStoreRecord? bestMatch = vectorStoreSearchResult.FirstOrDefault(x => x.FileName.Equals(responseDocumentMatch.Result.FileName, StringComparison.CurrentCultureIgnoreCase));

                    newEntry.PotentialAttachment = bestMatch?.FileName;

                    if (string.IsNullOrWhiteSpace(newEntry.Description) && bestMatch != null)
                    {
                        notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records (Determine what was purchased from {newEntry.Company})");
                        ChatClientAgentRunResponse<InvoiceResult> response = await invoiceDetailsAgent.RunAsync<InvoiceResult>("What was purchased here: " + bestMatch.Content);
                        newEntry.Description = response.Result.ProductPurchased;
                        if (string.IsNullOrWhiteSpace(newEntry.Category))
                        {
                            newEntry.Category = response.Result.Category;
                        }
                    }
                }
            }

            existing.Add(newEntry);
        }

        step++;
        notifyProgress.Invoke($"{step}/{totalSteps}: Merge and save data...");
        financialRecordCommand.Save(rootDataFolder, year, account, existing);
        return existing;
    }

    [UsedImplicitly]
    private class DividendCompanyResult
    {
        [Description("Just the company name, nothing else")]
        public required string CompanyName { get; set; }
    }

    [UsedImplicitly]
    private class DocumentMatch
    {
        public required string FileName { get; set; }
    }

    [UsedImplicitly]
    private class InvoiceResult
    {
        [Description("Just the product-name/type of product")]
        public required string ProductPurchased { get; set; }

        [Description("Choose among the following categories ['It Udstyr','Kontorudstyr', or 'Services']")] //todo: make this part of instructions instead and from a configurable source
        public required string Category { get; set; }
    }
}

public class PdfCleanUp
{
    public string? Issuer { get; set; }
    public DateTime? Date { get; set; }
    public int Month { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    public string ToString(string rawData)
    {
        StringBuilder builder = new();
        builder.AppendLine("<PdfContent>");
        builder.AppendLine($"<Issuer>{Issuer ?? "???"}</Issuer>");
        builder.AppendLine($"<Date>{Date?.ToString("yyyyMMdd") ?? "???"}</Date>");
        builder.AppendLine($"<Month>{Month}</Date>");
        builder.AppendLine($"<Amount>{Amount?.ToString(CultureInfo.InvariantCulture) ?? "???"} {Currency}</Amount>");
        builder.AppendLine($"<RawData>{rawData}</RawData>");
        builder.AppendLine("</PdfContent>");

        return builder.ToString();
    }
}

public class VectorStoreRecord
{
    [VectorStoreKey]
    public required string Id { get; set; }

    [VectorStoreData]
    public string? Issuer { get; set; }

    [VectorStoreData]
    public string? Date { get; set; }

    [VectorStoreData]
    public int Month { get; set; }

    [VectorStoreData]
    public int? Amount { get; set; }

    [VectorStoreData]
    public required string Content { get; set; }

    [VectorStoreData]
    public required string FileName { get; set; }

    [VectorStoreVector(1536)]
    [UsedImplicitly]
    public string Vector => Content;

    public override string ToString()
    {
        return $"<pdf filename=\"{FileName}\">{Content}</pdf>";
    }
}