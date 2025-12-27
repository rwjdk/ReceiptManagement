using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using JetBrains.Annotations;
using Microsoft.Agents.AI;
using SimpleRag;
using SimpleRag.DataSources;
using SimpleRag.DataSources.Pdf;
using SimpleRag.VectorStorage.Models;
using System.ComponentModel;
using SimpleRag.DataProviders;

#pragma warning disable RAG003

namespace Logic;

public class Controller(
    AzureOpenAIAgentFactory agentFactory,
    FinancialRecordQuery financialRecordQuery,
    FinancialRecordCommand financialRecordCommand,
    BankFileQuery bankFileQuery,
    Ingestion ingestion,
    Search search,
    IServiceProvider serviceProvider)
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


        CollectionId collectionId = new("Finance");
        PdfDataSource pdfDataSource = new(serviceProvider)
        {
            CollectionId = collectionId,
            Id = new SourceId("PDFS"),
            Path = pathToUnprocessedPdfs,
            FilesProvider = new LocalFilesDataProvider(),
        };

        int unprocessedFileCount = Directory.GetFiles(pathToUnprocessedPdfs, "*.pdf", SearchOption.AllDirectories).Length;
        step++;
        notifyProgress.Invoke($"{step}/{totalSteps}: Ingesting {unprocessedFileCount} unprocessed PDFs into a vector-store");
        await ingestion.IngestAsync([pdfDataSource]);

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
                if (matchResult.NeedDividedCompanyMatch)
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
                    notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records (Finding Related PDF to {newEntry.BankEntry.Text})");
                    SearchResult searchResult = await search.SearchAsync(new SearchOptions
                    {
                        NumberOfRecordsBack = 1, //todo - get more records back and let an LLM determine which is the most likely match
                        SearchQuery = $"Date: {newEntry.BankEntry.Date} - Text: {newEntry.BankEntry.Text} - Amount: {newEntry.BankEntry.Amount} DKK - Company: {newEntry.Company} - Description: {newEntry.Description}",
                        CollectionId = collectionId
                    });
                    VectorEntity vectorEntity = searchResult.Entities[0].Record;

                    string filename = vectorEntity.SourcePath;
                    if (filename.StartsWith("\\")) //todo - remove when fixed in SimpleRag
                    {
                        filename = filename[1..];
                    }

                    newEntry.PotentialAttachment = filename;

                    if (string.IsNullOrWhiteSpace(newEntry.Description))
                    {
                        //Todo - find all pages of same doc and give to LLM
                        notifyProgress.Invoke($"{step}/{totalSteps}: Processing {fromBankEntries.Length} potentially new records (Determine what was purchased from {newEntry.Company})");
                        ChatClientAgentRunResponse<InvoiceResult> response = await invoiceDetailsAgent.RunAsync<InvoiceResult>("What was purchased here: " + vectorEntity.Content);
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
    private class InvoiceResult
    {
        [Description("Just the product-name/type of product")]
        public required string ProductPurchased { get; set; }

        [Description("Choose among the following categories ['It Udstyr','Kontorudstyr', or 'Services']")] //todo: make this part of instructions instead and from a configurable source
        public required string Category { get; set; }
    }
}