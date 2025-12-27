using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using JetBrains.Annotations;
using Logic.Queries;
using Microsoft.Agents.AI;
using SimpleRag;
using SimpleRag.DataSources;
using SimpleRag.DataSources.Pdf;
using SimpleRag.VectorStorage.Models;
using System.ComponentModel;
using Logic.Commands;
using SimpleRag.DataProviders;

#pragma warning disable RAG003

namespace Logic;

public class FooQuery(
    AzureOpenAIAgentFactory agentFactory,
    FinancialRecordQuery financialRecordQuery,
    FinancialRecordCommand financialRecordCommand,
    BankFileQuery bankFileQuery,
    Ingestion ingestion,
    Search search,
    IServiceProvider serviceProvider)
{
    public async Task<List<FinancialRecord>> Foo(List<MatchRule> matchRules)
    {
        BankEntry[] entries = bankFileQuery.ReadEntries(@"C:\Test\OneMonth.csv");

        string receiptPath = @"C:\Test\receipts";

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
            Path = receiptPath,
            FilesProvider = new LocalFilesDataProvider(),
        };

        await ingestion.IngestAsync([pdfDataSource], new IngestionOptions
        {
            OnProgressNotification = notification => Console.WriteLine(notification.GetFormattedMessage())
        });


        int year = 2025;
        string account = "main";
        string root = @"C:\Test\Data";
        List<FinancialRecord> existing = financialRecordQuery.GetExisting(root, year, account);
        FinancialRecord[] fromBankEntries = await financialRecordQuery.FromBankEntries(entries, matchRules.ToArray());

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
                if (matchResult.NeedYieldCompanyMatch)
                {
                    Console.WriteLine($"Finding Yield Match for {newEntry.Company}: {newEntry.Description}");
                    string description = newEntry.Description ?? string.Empty;
                    ChatClientAgentRunResponse<YieldCompanyResult> response = await yieldCompanyAgent.RunAsync<YieldCompanyResult>("What company does this refer to?: " + newEntry.BankEntry.Text);
                    string yieldCompany = response.Result.CompanyName;
                    description = description.Replace("<COMPANY>", yieldCompany);
                    newEntry.Description = description;
                }

                if (matchResult.NeedAttachment)
                {
                    Console.WriteLine($"Finding Attachment Match for {newEntry.Company}: {newEntry.Description}");
                    SearchResult searchResult = await search.SearchAsync(new SearchOptions
                    {
                        NumberOfRecordsBack = 1,
                        SearchQuery = $"Date: {newEntry.BankEntry.Date} - Text: {newEntry.BankEntry.Text} - Amount: {newEntry.BankEntry.Amount} DKK - Company: {newEntry.Company}- Description: {newEntry.Description}",
                        CollectionId = collectionId
                    });
                    VectorEntity vectorEntity = searchResult.Entities[0].Record;


                    string sourcePath = vectorEntity.SourcePath;
                    if (sourcePath.StartsWith("\\"))
                    {
                        sourcePath = sourcePath[1..];
                    }

                    newEntry.PotentialAttachment = sourcePath;

                    if (string.IsNullOrWhiteSpace(newEntry.Description))
                    {
                        //Todo - find all pages of same doc and give to LLM
                        Console.WriteLine($"Determine what was purchased from {newEntry.Company}");
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

        financialRecordCommand.Save(root, year, account, existing);
        return existing;
    }

    [UsedImplicitly]
    private class YieldCompanyResult
    {
        [Description("Just the company name, nothing else")]
        public required string CompanyName { get; set; }
    }

    [UsedImplicitly]
    private class InvoiceResult
    {
        [Description("Just the product-name/type of product")]
        public required string ProductPurchased { get; set; }

        [Description("Choose among the following categories ['It Udstyr','Kontorudstyr', or 'Services']")]
        public required string Category { get; set; }
    }
}