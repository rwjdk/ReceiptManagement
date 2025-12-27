using AgentFrameworkToolkit.AzureOpenAI;
using Logic;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using SimpleRag;
using SimpleRag.VectorStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#pragma warning disable RAG003

Console.Clear();

BankFileQuery bankFileQuery = new BankFileQuery();

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

VectorStoreConfiguration vectorStoreConfiguration = new("UnprocessedAttachments");
HostApplicationBuilder builder = Host.CreateApplicationBuilder();

AzureOpenAIConnection connection = new AzureOpenAIConnection
{
    Endpoint = "https://sensum365ai.openai.azure.com/",
    ApiKey = "136brUgziYdwzkHcJt8yeWmwsnKtbIvTIrhzBQrkTpWF8D71b6BoJQQJ99BJACfhMk5XJ3w3AAABACOGrq0S"
};
builder.Services.AddMemoryCache();
builder.Services.AddAzureOpenAIAgentFactory(connection);
builder.Services.AddAzureOpenAIEmbeddingFactory(connection);
builder.Services.AddEmbeddingGenerator(provider =>
{
    AzureOpenAIEmbeddingFactory embeddingFactory = provider.GetRequiredService<AzureOpenAIEmbeddingFactory>();
    return embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small");
});

builder.Services.AddSimpleRag(vectorStoreConfiguration, provider => new InMemoryVectorStore(new InMemoryVectorStoreOptions
{
    EmbeddingGenerator = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()
}));

IHost app = builder.Build();

AzureOpenAIAgentFactory agentFactory = app.Services.GetRequiredService<AzureOpenAIAgentFactory>();
Search search = app.Services.GetRequiredService<Search>();
Ingestion ingestion = app.Services.GetRequiredService<Ingestion>();

FinancialRecordQuery financialRecordQuery = new(agentFactory);
FinancialRecordCommand financialRecordCommand = new(financialRecordQuery);


Controller controller = new(agentFactory, financialRecordQuery, financialRecordCommand, bankFileQuery, ingestion, search, app.Services);

int year = 2025;
string account = "main";
string rootDataFolder = @"C:\Test";
string newDateRangeCsv = @"C:\Test\year.csv";
string pathToUnprocessedPdfs = @"C:\Test\UnprocessedPdfs";
List<FinancialRecord> records = await controller.AddNewEntries(matchRules, newDateRangeCsv, pathToUnprocessedPdfs, rootDataFolder, year, account);

FinancialRecord[] matched = records.Where(x => x.MatchResults.Length == 1).OrderBy(x => x.BankEntry.Text).ToArray();
FinancialRecord[] notMatched = records.Where(x => x.MatchResults.Length != 1).OrderBy(x => x.BankEntry.Text).ToArray();

//Todo - write new final CSV everytime new data is approved