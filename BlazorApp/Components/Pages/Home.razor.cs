using JetBrains.Annotations;
using Logic;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using MudBlazor;
using System.Text;
using AgentFrameworkToolkit.AzureOpenAI;

namespace BlazorApp.Components.Pages;

[UsedImplicitly]
public partial class Home(FinancialRecordQuery financialRecordQuery, AccountCommand accountCommand, AccountQuery accountQuery, AzureOpenAIEmbeddingFactory embeddingFactory)
{
    private FinancialRecord? _selectedRecord;
    private string _loadingStatus = string.Empty;
    private Account? _selectedAccount;
    private Account[]? _accounts;
    private SqliteCollection<string, VectorStoreRecord>? _vectorStoreCollection;

    private List<MatchRule> matchRules =
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
            MatchRuleType.TextRegEx, 0, 0, ["^Telenor.dk"], "Telenor", "Telefon (<MONTH>)", "Services", true),

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
            MatchRuleType.TextRegEx, 0, 0, ["^Fonds 20", "^Salg af aktier 20", "^Salg investbev 20"], "Nordea", null, "Investeringer", false),

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
            MatchRuleType.TextRegEx, 0, 0, ["^proshop.dk"], "Proshop", null, "IT Udstyr", true),

        new("Private banking aft.",
            MatchRuleType.TextRegEx, 0, 0, ["^Private banking aft.", "^Salg investbev", "^Køb investbev"], "Nordea", "Private Banking Gebyr", "Investeringer", false),

        new("Renter",
            MatchRuleType.TextRegEx, 0, 0, ["^Renter"], "Nordea", "Renter", "Renter", false),
    ];

    protected override async Task OnInitializedAsync()
    {
        SqliteVectorStore vectorStore = new SqliteVectorStore("Data Source=" + Paths.RootDataFolder + "\\vector-store.db", new SqliteVectorStoreOptions
        {
            EmbeddingGenerator = embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small")
        });

        _vectorStoreCollection = vectorStore.GetCollection<string, VectorStoreRecord>("Data");
        await _vectorStoreCollection.EnsureCollectionExistsAsync();

        RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        _accounts = accountQuery.GetAccounts(Paths.RootDataFolder);
        if (_accounts.Length != 0)
        {
            _selectedAccount = _accounts[0];
        }
    }

    private void NotifyProgress(string obj)
    {
        _loadingStatus = obj;
        StateHasChanged();
    }

    private void SelectRow(DataGridRowClickEventArgs<FinancialRecord> arg)
    {
        _selectedRecord = arg.Item;
    }

    private void ChangeAccount(Account account)
    {
        _selectedAccount = account;
    }

    private void CreateAccount()
    {
        //todo - dialog
        int year = DateTime.Today.Year;
        string accountName = "main";
        accountCommand.CreateAccount(Paths.RootDataFolder, year, accountName);
        RefreshAccounts();
    }

    private void CreateAccountYear()
    {
        if (_selectedAccount == null)
        {
            return;
        }

        //todo - dialog
        int newYear = _selectedAccount.Year + 1;
        accountCommand.CreateAccount(Paths.RootDataFolder, newYear, _selectedAccount.Name);
        RefreshAccounts();
    }

    private async Task UploadFile(IBrowserFile? file)
    {
        if (_selectedAccount == null || file == null)
        {
            return;
        }

        //todo - need a busy state system that disable buttons while data is loading
        await using Stream stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        using StreamReader reader = new(stream, Encoding.UTF8);
        string bankFileContent = await reader.ReadToEndAsync();

        List<FinancialRecord> newRecords = await financialRecordQuery.GetNewRecords(NotifyProgress, _vectorStoreCollection!, matchRules, bankFileContent, Paths.PathToUnprocessedPdfs, _selectedAccount);
        //todo - inform how many new records
        _selectedAccount.Records.AddRange(newRecords);
        _loadingStatus = string.Empty;
        accountCommand.UpdateAccount(Paths.RootDataFolder, _selectedAccount);
    }
}