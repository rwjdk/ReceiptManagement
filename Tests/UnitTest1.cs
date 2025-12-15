using Logic;
using Logic.Queries;
using static System.Net.Mime.MediaTypeNames;

namespace Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        BankEntry[] entries = new BankFileQuery().ReadEntries("TestData\\year.csv");

        List<MatchRule> matchRules =
        [
            new("Danløn Lønservice",
                MatchRuleType.TextRegExAndAmountRange, -31.25M, -31.25M, "^Overførsel ID", "Danløn", "Lønservice (<MONTH>)", "Lønninger", true),

            new("Danløn Egen Løn",
                MatchRuleType.TextRegEx, 0, 0, "^Lønoverførsel ID", "Danløn", "Egen Løn (<MONTH>)", "Lønninger", false),

            new("Danløn Egen Løn Skat",
                MatchRuleType.TextRegExAndAmountRange, -12_500, -10_000, "^Info-overførsel", "Danløn", "Egen Løn Skat (<MONTH>)", "Lønninger", false),

            new("Danløn Egen Løn Pension",
                MatchRuleType.TextRegExAndAmountRange, -6_000, -5_000, "^Info-overførsel", "Danløn", "Egen Løn Pension (<MONTH>)", "Lønninger", false),

            new("Udbytte fra Aktier",
                MatchRuleType.TextRegEx, 0, 0, "^Udbytte", "Nordea", "Udbytte (TODO)", "Investeringer", false),

            new("Telenor",
                MatchRuleType.TextRegEx, 0, 0, "^Telenor.dk", "Telenor", "Telefon (<MONTH>)", "Services", false),

            new("Microsoft Azure (1)",
                MatchRuleType.TextRegEx, 0, 0, "^MicrosoftG", "Microsoft", "Azure Subscription (<MONTH>)", "Services", true),

            new("Microsoft Azure (2)",
                MatchRuleType.TextRegEx, 0, 0, "^Microsoft-G", "Microsoft", "Azure Subscription (<MONTH>)", "Services", true),

            new("Microsoft Azure (3)",
                MatchRuleType.TextRegEx, 0, 0, "^MICROSOFTÆG", "Microsoft", "Azure Subscription (<MONTH>)", "Services", true),

            new("Microsoft Office",
                MatchRuleType.TextRegEx, 0, 0, "^MICROSOFT\\*MICROSOFT", "Microsoft", "Office 365 Subscription", "Services", true),

            new("FastSpeed",
                MatchRuleType.TextRegEx, 0, 0, "^fastspeed.dk", "FastSpeed", "Internet (<QUARTER>)", "Services", true),

            new("Private Banking Gebyr",
                MatchRuleType.TextRegEx, 0, 0, "^Gebyr af depot", "Nordea", "Private Banking Gebyr (<QUARTER>)", "Investeringer", false),

            new("Google Cloud",
                MatchRuleType.TextRegEx, 0, 0, "^GOOGLE\\*CLOUD", "Google", "Google Cloud Platform (AI <MONTH>)", "Services", true),

            new("Google Cloud",
                MatchRuleType.TextRegEx, 0, 0, "^GOOGLE CLOUD", "Google", "Google Cloud Platform (AI <MONTH>)", "Services", true),

            new("Nordea Gebyrer",
                MatchRuleType.TextRegEx, 0, 0, "^Gebyr, overf", "Nordea", "Gebyrer", "Gebyrer", false),

            new("Nordea Egen Salg af Aktier",
                MatchRuleType.TextRegEx, 0, 0, "^Fonds 20", "Nordea", "Salg af Aktier (TODO)", "Investeringer", false),

            new("Nordea Egen Salg af Aktier (2)",
                MatchRuleType.TextRegEx, 0, 0, "^Salg af aktier 20", "Nordea", "Salg af Aktier (TODO)", "Investeringer", false),

            new("Nordea Egen Salg af Aktier (3)",
                MatchRuleType.TextRegEx, 0, 0, "^Salg investbev 20", "Nordea", "Salg af Aktier (TODO)", "Investeringer", false),

            new("OpenAI",
                MatchRuleType.TextRegEx, 0, 0, "^OPENAI", "OpenAI", "AI Services", "Services", true),

            new("ANTHROPIC",
                MatchRuleType.TextRegEx, 0, 0, "^ANTHROPIC", "ANTHROPIC", "AI Services", "Services", true),

            new("ANTHROPIC",
                MatchRuleType.TextRegEx, 0, 0, "^XAI LLC", "XAI", "AI Services", "Services", true),

            new("Samplet Betaling",
                MatchRuleType.TextRegEx, 0, 0, "^Bs betaling ATP - SAMLET BETALIN", "Sample Betaling", "Samlet Betaling", "Services", false),

            new("Amazon",
                MatchRuleType.TextRegEx, 0, 0, "^AMAZON", "Amazon", "TODO", "TODO", true),

            new("APPLE",
                MatchRuleType.TextRegEx, 0, 0, "^APPLE", "Apple", "TODO", "TODO", true),

            new("JetBrains",
                MatchRuleType.TextRegEx, 0, 0, "^JetBrains", "JetBrains", "Resharper Ultimate Subscription", "Services", true),

            new("LastPass",
                MatchRuleType.TextRegEx, 0, 0, "^LASTPASS", "LastPass", "Password Service", "Services", true),

            new("UBISECURE",
                MatchRuleType.TextRegEx, 0, 0, "^UBISECURE", "Ubisecure Oy", "Fornyelse af LEI", "Services", true),

            new("PORKBUN",
                MatchRuleType.TextRegEx, 0, 0, "^PORKBUN.COM", "Porkbun", "Domæne-fornyelse", "Services", true),

            new("proshop.dk",
                MatchRuleType.TextRegEx, 0, 0, "^proshop.dk", "Proshop", "TODO", "IT Udstyr", true),

            new("Private banking aft.",
                MatchRuleType.TextRegEx, 0, 0, "^Private banking aft.", "Nordea", "Private Banking Gebyr", "Investeringer", false),

            new("Renter",
                MatchRuleType.TextRegEx, 0, 0, "^Renter", "Nordea", "Renter", "Renter", false),

            new("Private Bankning (1)",
                MatchRuleType.TextRegEx, 0, 0, "^Salg investbev", "Nordea", "Private Banking", "Investeringer", false),

            new("Private Bankning (2)",
                MatchRuleType.TextRegEx, 0, 0, "^Køb investbev", "Nordea", "Private Banking", "Investeringer", false),
        ];

        FinancialRecordQuery financialRecordQuery = new FinancialRecordQuery();

        FinancialRecord[] existing = financialRecordQuery.GetExisting(2025, "main");
        FinancialRecord[] fromBankEntries = financialRecordQuery.FromBankEntries(entries, matchRules.ToArray());

        foreach (FinancialRecord newEntry in fromBankEntries.Reverse())
        {
            if (existing.Any(x => x.Date == newEntry.Date && x.Amount == newEntry.Amount && x.BankText == newEntry.BankText))
            {
                continue;
            }

            //todo add
        }


        FinancialRecord[] matched = fromBankEntries.Where(x => x.Matched).OrderBy(x => x.BankText).ToArray();
        FinancialRecord[] notMatched = fromBankEntries.Where(x => !x.Matched).OrderBy(x => x.BankText).ToArray();
    }
}