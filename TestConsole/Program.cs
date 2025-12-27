using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using JetBrains.Annotations;
using Logic;
using Logic.Commands;
using Logic.Queries;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using SimpleRag;
using SimpleRag.DataProviders;
using SimpleRag.DataSources;
using SimpleRag.DataSources.Pdf;
using SimpleRag.DataSources.Pdf.Chunker;
using SimpleRag.VectorStorage;
using SimpleRag.VectorStorage.Models;
using System.ComponentModel;
using Microsoft.Extensions.VectorData;

#pragma warning disable RAG003

Console.Clear();

BankEntry[] entries = new BankFileQuery().ReadEntries("TestData\\year.csv");

//Todo - add multi-regex match instead of just one expected pattern
List<MatchRule> matchRules =
[
    new("Danløn Lønservice",
        MatchRuleType.TextRegExAndAmountRange, -31.25M, -31.25M, ["^Overførsel ID"], "Danløn", "Lønservice (<MONTH>)", "Lønninger", true),

    new("Danløn Egen Løn",
        MatchRuleType.TextRegEx, 0, 0, ["^Lønoverførsel ID"], "Danløn", "Egen Løn (<MONTH>)", "Lønninger", false),

    new("Danløn Egen Løn Skat",
        MatchRuleType.TextRegExAndAmountRange, -12_500, -10_000, ["^Info-overførsel"], "Danløn", "Egen Løn Skat (<MONTH>)", "Lønninger", false),

    new("Danløn Egen Løn Pension",
        MatchRuleType.TextRegExAndAmountRange, -6_000, -5_000, ["^Info-overførsel"], "Danløn", "Egen Løn Pension (<MONTH>)", "Lønninger", false),

    new("Udbytte fra Aktier",
        MatchRuleType.TextRegEx, 0, 0, ["^Udbytte"], "Nordea", "Udbytte (<COMPANY>)", "Investeringer", false, true),

    new("Telenor",
        MatchRuleType.TextRegEx, 0, 0, ["^Telenor.dk"], "Telenor", "Telefon (<MONTH>)", "Services", false),

    new("Microsoft Azure",
        MatchRuleType.TextRegEx, 0, 0, ["^MicrosoftG", "^Microsoft-G", "^MICROSOFTÆG"], "Microsoft", "Azure Subscription (<MONTH>)", "Services", true),

    new("Microsoft Office",
        MatchRuleType.TextRegEx, 0, 0, ["^MICROSOFT\\*MICROSOFT"], "Microsoft", "Office 365 Subscription", "Services", true),

    new("FastSpeed",
        MatchRuleType.TextRegEx, 0, 0, ["^fastspeed.dk"], "FastSpeed", "Internet (<QUARTER>)", "Services", true),

    new("Private Banking Gebyr",
        MatchRuleType.TextRegEx, 0, 0, ["^Gebyr af depot"], "Nordea", "Private Banking Gebyr (<QUARTER>)", "Investeringer", false),

    new("Google Cloud",
        MatchRuleType.TextRegEx, 0, 0, ["^GOOGLE\\*CLOUD", "^GOOGLE CLOUD"], "Google", "Google Cloud Platform (AI <MONTH>)", "Services", true),

    new("Nordea Gebyrer",
        MatchRuleType.TextRegEx, 0, 0, ["^Gebyr, overf"], "Nordea", "Gebyrer", "Gebyrer", false),

    new("Nordea Egen Salg af Aktier",
        MatchRuleType.TextRegEx, 0, 0, ["^Fonds 20", "^Salg af aktier 20", "^Salg investbev 20"], "Nordea", "Salg af Aktier (TODO)", "Investeringer", false),

    new("OpenAI",
        MatchRuleType.TextRegEx, 0, 0, ["^OPENAI"], "OpenAI", "AI Services", "Services", true),

    new("ANTHROPIC",
        MatchRuleType.TextRegEx, 0, 0, ["^ANTHROPIC"], "ANTHROPIC", "AI Services", "Services", true),

    new("XAI",
        MatchRuleType.TextRegEx, 0, 0, ["^XAI LLC"], "XAI", "AI Services", "Services", true),

    new("Samplet Betaling",
        MatchRuleType.TextRegEx, 0, 0, ["^Bs betaling ATP - SAMLET BETALIN"], "Sample Betaling", "Samlet Betaling", "Services", false),

    new("Amazon",
        MatchRuleType.TextRegEx, 0, 0, ["^AMAZON"], "Amazon", null, null, true),

    new("APPLE",
        MatchRuleType.TextRegEx, 0, 0, ["^APPLE"], "Apple", null, null, true),

    new("JetBrains",
        MatchRuleType.TextRegEx, 0, 0, ["^JetBrains"], "JetBrains", "Resharper Ultimate Subscription", "Services", true),

    new("LastPass",
        MatchRuleType.TextRegEx, 0, 0, ["^LASTPASS"], "LastPass", "Password Service", "Services", true),

    new("UBISECURE",
        MatchRuleType.TextRegEx, 0, 0, ["^UBISECURE"], "Ubisecure Oy", "Fornyelse af LEI", "Services", true),

    new("PORKBUN",
        MatchRuleType.TextRegEx, 0, 0, ["^PORKBUN.COM"], "Porkbun", "Domæne-fornyelse", "Services", true),

    new("proshop.dk",
        MatchRuleType.TextRegEx, 0, 0, ["^proshop.dk"], "Proshop", "TODO", "IT Udstyr", true),

    new("Private banking aft.",
        MatchRuleType.TextRegEx, 0, 0, ["^Private banking aft.", "^Salg investbev", "^Køb investbev"], "Nordea", "Private Banking Gebyr", "Investeringer", false),

    new("Renter",
        MatchRuleType.TextRegEx, 0, 0, ["^Renter"], "Nordea", "Renter", "Renter", false),
];

string receiptPath = @"C:\Test\receipts";

AzureOpenAIConnection connection = new AzureOpenAIConnection
{
    Endpoint = "https://sensum365ai.openai.azure.com/",
    ApiKey = "136brUgziYdwzkHcJt8yeWmwsnKtbIvTIrhzBQrkTpWF8D71b6BoJQQJ99BJACfhMk5XJ3w3AAABACOGrq0S"
};
AzureOpenAIAgentFactory agentFactory = new(connection);
AzureOpenAIEmbeddingFactory embeddingFactory = new(connection);


FinancialRecordQuery financialRecordQuery = new(agentFactory);
FinancialRecordCommand financialRecordCommand = new(financialRecordQuery);

AzureOpenAIAgent yieldCompanyAgent = agentFactory.CreateAgent(new AgentOptions()
{
    Model = OpenAIChatModels.Gpt52,
    Instructions = "You are a Stock Expert",
    ReasoningEffort = OpenAIReasoningEffort.Minimal
});

AzureOpenAIAgent invoiceDetailsAgent = agentFactory.CreateAgent(new AgentOptions()
{
    Model = OpenAIChatModels.Gpt5Mini,
    Instructions = "You are an Expert in analyzing raw PDF Data and extracting what was bought",
    ReasoningEffort = OpenAIReasoningEffort.Minimal
});

VectorStoreConfiguration vectorStoreConfiguration = new("UnprocessedAttachments");


IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small");
VectorStore vectorStore = new InMemoryVectorStore(new InMemoryVectorStoreOptions
{
    EmbeddingGenerator = embeddingGenerator
});

VectorStoreQuery vectorStoreQuery = new VectorStoreQuery(embeddingGenerator, vectorStore, vectorStoreConfiguration, null);


CollectionId collectionId = new("Finance");
PdfDataSource pdfDataSource = new(new PdfChunker(), new VectorStoreCommand(vectorStore, vectorStoreQuery, vectorStoreConfiguration))
{
    CollectionId = collectionId,
    Id = new SourceId("PDFS"),
    Path = receiptPath,
    FilesProvider = new LocalFilesDataProvider(),
};
Ingestion ingestion = new();
await ingestion.IngestAsync([pdfDataSource], new IngestionOptions
{
    OnProgressNotification = notification => Console.WriteLine(notification.GetFormattedMessage())
});

Search search = new Search(vectorStoreQuery);

int year = 2025;
string account = "main";
List<FinancialRecord> existing = financialRecordQuery.GetExisting(year, account);
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
            newEntry.PotentialAttachment = vectorEntity.SourcePath;

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

financialRecordCommand.Save(year, account, existing);

FinancialRecord[] matched = existing.Where(x => x.MatchResults.Length == 1).OrderBy(x => x.BankEntry.Text).ToArray();
FinancialRecord[] notMatched = existing.Where(x => x.MatchResults.Length != 1).OrderBy(x => x.BankEntry.Text).ToArray();

[UsedImplicitly]
class YieldCompanyResult
{
    [Description("Just the company name, nothing else")]
    public required string CompanyName { get; set; }
}

[UsedImplicitly]
class InvoiceResult
{
    [Description("Just the product-name/type of product")]
    public required string ProductPurchased { get; set; }

    [Description("Choose among the following categories ['It Udstyr','Kontorudstyr', or 'Services']")]
    public required string Category { get; set; }
}