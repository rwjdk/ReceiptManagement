using System.Text.RegularExpressions;
using SQLitePCL;

namespace Logic;

public record MatchRule(string RuleName, MatchRuleType Type, decimal ExpectedAmountMin, decimal ExpectedAmountMax, string[] ExpectedRegEx, string Company, string? Description, string? Category, bool NeedAttachment, bool NeedYieldCompanyMatch = false)
{
    public MatchResult? Match(BankEntry bankEntry)
    {
        switch (Type)
        {
            case MatchRuleType.TextRegExAndAmountRange:
                foreach (string regEx in ExpectedRegEx)
                {
                    if (bankEntry.Amount >= ExpectedAmountMin && bankEntry.Amount <= ExpectedAmountMax && Regex.IsMatch(bankEntry.Text, regEx, RegexOptions.IgnoreCase))
                    {
                        return CreateMatch(bankEntry, $"Matched on Amount ({ExpectedAmountMin}) and RegEx ('{ExpectedRegEx}')");
                    }
                }

                break;
            case MatchRuleType.TextRegEx:
                foreach (string regEx in ExpectedRegEx)
                {
                    if (Regex.IsMatch(bankEntry.Text, regEx, RegexOptions.IgnoreCase))
                    {
                        return CreateMatch(bankEntry, $"Matched on Text ('{ExpectedRegEx}')");
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return null;
    }

    private MatchResult CreateMatch(BankEntry bankEntry, string notes)
    {
        return new MatchResult(
            notes,
            ReplaceKeywords(Company, bankEntry),
            ReplaceKeywords(Description, bankEntry),
            ReplaceKeywords(Category, bankEntry),
            NeedAttachment,
            NeedYieldCompanyMatch);
    }

    private string? ReplaceKeywords(string? text, BankEntry bankEntry)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = text.Replace("<MONTH>", bankEntry.Date.ToString("MMM"));
        text = text.Replace("<QUARTER>", "Q" + (((bankEntry.Date.Month - 1) / 3) + 1));
        return text;
    }
}