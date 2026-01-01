using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using JetBrains.Annotations;
using Logic.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Logic;

//todo- refactor and clean
public class AccountController(AzureOpenAIAgentFactory agentFactory, AzureOpenAIEmbeddingFactory embeddingFactory)
{
    public async Task ReprocessRecord(AccountRecord accountRecord, MatchRule[] matchRules)
    {
        await Task.CompletedTask;
        AccountRecord record = FromBankEntries([accountRecord.BankEntry], matchRules)[0];
        accountRecord.Company = record.Company;
        accountRecord.Description = record.Description;
        accountRecord.Category = record.Category;
        accountRecord.MatchResults = record.MatchResults;

        //todo - reprocess AI things
    }

    public async Task<List<AccountRecord>> GetNewRecords(
        int year,
        Action<string> notifyProgress,
        MatchRule[] matchRules,
        string newContent,
        string pathToUnprocessedPdfs,
        Account account)
    {
        SqliteVectorStore vectorStore = new($"Data Source={Path.GetTempPath()}\\vector-store-{year}.db", new SqliteVectorStoreOptions
        {
            EmbeddingGenerator = embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small")
        });

        SqliteCollection<string, VectorStoreRecord> vectorStoreCollection = vectorStore.GetCollection<string, VectorStoreRecord>("Data");
        await vectorStoreCollection.EnsureCollectionExistsAsync();

        List<AccountRecord> newRecords = [];
        BankEntry[] entries = ReadBankEntries(newContent, account).Where(x => x.Date.Year == year).ToArray();
        AccountRecord[] fromBankEntries = FromBankEntries(entries, matchRules.ToArray());
        List<AccountRecord> toProcess = [];
        foreach (AccountRecord newRecord in fromBankEntries.Reverse())
        {
            if (account.Records.Any(x => x.BankEntry == newRecord.BankEntry))
            {
                continue;
            }

            toProcess.Add(newRecord);
        }

        if (toProcess.Count == 0)
        {
            return [];
        }

        notifyProgress.Invoke($"Found {toProcess.Count} new records to process");

        AzureOpenAIAgent dividendCompanyAgent = agentFactory.CreateAgent(new AgentOptions
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


        List<VectorStoreRecord> existingRecords = [];
        await foreach (VectorStoreRecord storeRecord in vectorStoreCollection.GetAsync(filter: record => record.Id != "", top: int.MaxValue))
        {
            existingRecords.Add(storeRecord);
        }

        string[] existingIds = existingRecords.Select(x => x.Id).ToArray();
        string[] unprocessedPdfs = Directory.GetFiles(pathToUnprocessedPdfs, "*.pdf", SearchOption.AllDirectories);

        List<string> activeIds = [];
        List<string> toIngest = [];
        foreach (string pdfPath in unprocessedPdfs)
        {
            activeIds.Add(pdfPath);
            if (existingIds.Contains(pdfPath))
            {
                continue;
            }

            toIngest.Add(pdfPath);
        }

        int counter = 0;
        foreach (string pdfPath in toIngest)
        {
            counter++;
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

            notifyProgress.Invoke($"- [{counter}/{toIngest.Count}] Ingesting unprocessed PDF to vector-store '{Path.GetFileName(pdfPath)}'");
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

                PdfCleanUp result = response.Result;
                await vectorStoreCollection.UpsertAsync(new VectorStoreRecord
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
                await vectorStoreCollection.UpsertAsync(new VectorStoreRecord
                {
                    Id = pdfPath,
                    Content = pdfText,
                    FileName = Path.GetFileName(pdfPath)
                });
            }
        }

        IEnumerable<string> deadIds = existingIds.Except(activeIds);
        await vectorStoreCollection.DeleteAsync(deadIds);

        int nextLineNum = account.Records.MaxBy(x => x.LineNum)?.LineNum ?? 0;

        counter = 1;
        foreach (AccountRecord newRecord in toProcess)
        {
            notifyProgress.Invoke($"- [{counter}/{toProcess.Count}] Processing new record 'Date: {newRecord.BankEntry.Date.ToString("dd. MMM")} Amount: {newRecord.BankEntry.Amount:N2} - Text: {newRecord.BankEntry.Text}'");
            counter++;
            if (newRecord.MatchResults.Length == 1)
            {
                //Good match
                MatchResult matchResult = newRecord.MatchResults[0];
                if (matchResult.NeedDividedCompanyMatch) //todo - add back in
                {
                    string description = newRecord.Description ?? string.Empty;
                    ChatClientAgentRunResponse<DividendCompanyResult> response = await dividendCompanyAgent.RunAsync<DividendCompanyResult>("What company does this refer to?: " + newRecord.BankEntry.Text);
                    string yieldCompany = response.Result.CompanyName;
                    description = description.Replace("<COMPANY>", yieldCompany);
                    newRecord.Description = description;
                }

                if (matchResult.NeedAttachment)
                {
                    List<VectorStoreRecord> vectorStoreSearchResult = [];
                    string query = newRecord.ToString();
                    await foreach (VectorSearchResult<VectorStoreRecord> result in vectorStoreCollection.SearchAsync(query, 12, new VectorSearchOptions<VectorStoreRecord>
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

                    string whatFileOfThese = "What File of these: " + searchResult + $" is the best match for this record: {newRecord} (Issuer, Amount (Might be different currency so adjust) and Month/Approximate Date is the best match-conditions)";
                    ChatClientAgentRunResponse<DocumentMatch> responseDocumentMatch = await invoiceDetailsAgent.RunAsync<DocumentMatch>(whatFileOfThese);
                    VectorStoreRecord? bestMatch = vectorStoreSearchResult.FirstOrDefault(x => x.FileName.Equals(responseDocumentMatch.Result.FileName, StringComparison.CurrentCultureIgnoreCase));

                    newRecord.Attachment = bestMatch?.FileName;

                    if (string.IsNullOrWhiteSpace(newRecord.Description) && bestMatch != null)
                    {
                        notifyProgress.Invoke($"-- Determine what was purchased from {newRecord.Company})");
                        ChatClientAgentRunResponse<InvoiceResult> response = await invoiceDetailsAgent.RunAsync<InvoiceResult>("What was purchased here: " + bestMatch.Content);
                        newRecord.Description = response.Result.ProductPurchased;
                        if (string.IsNullOrWhiteSpace(newRecord.Category))
                        {
                            newRecord.Category = response.Result.Category;
                        }
                    }
                }
            }

            newRecord.LineNum = nextLineNum;
            newRecords.Add(newRecord);
            nextLineNum++;
        }

        return newRecords;
    }

    public BankEntry[] ReadBankEntries(string content, Account account)
    {
        List<BankEntry> result = [];
        string[] lines = content.Split('\n');
        foreach (string line in lines.Skip(1))
        {
            string[] parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
            DateOnly date = DateOnly.ParseExact(parts[0], "yyyy/MM/dd");
            decimal amount = decimal.Parse(parts[1], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            string text = parts[3];
            decimal balance = decimal.Parse(parts[4], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            result.Add(new BankEntry(date, text, amount, balance));
        }

        return result.ToArray();
    }

    private AccountRecord[] FromBankEntries(BankEntry[] bankEntries, MatchRule[] matchRules)
    {
        List<AccountRecord> result = [];
        foreach (BankEntry bankEntry in bankEntries)
        {
            MatchResult[] matchResults = matchRules.Where(x => x.Match(bankEntry) != null).Select(x => x.Match(bankEntry)!).ToArray();
            switch (matchResults.Length)
            {
                case 1:
                    MatchResult match = matchResults[0];
                    result.Add(new AccountRecord(bankEntry, matchResults, match.Company, match.Description, match.Category, false));
                    break;
                default:
                    result.Add(new AccountRecord(bankEntry, matchResults, null, null, null, false));
                    break;
            }
        }

        return result.ToArray();
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

    [UsedImplicitly]
    private class PdfCleanUp
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

    [UsedImplicitly]
    private class VectorStoreRecord
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
}