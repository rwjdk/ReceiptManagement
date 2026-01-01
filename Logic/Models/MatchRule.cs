using System.Text.RegularExpressions;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Logic.Models;

public class MatchRule
{
    public MatchRule()
    {
        //Needed for config de-serialization
    }

    public MatchRule(string ruleName, MatchRuleType type, decimal expectedAmountMin, decimal expectedAmountMax, string[] expectedRegEx, string? company, string? description, string? category, bool needAttachment, bool needYieldCompanyMatch = false)
    {
        RuleName = ruleName;
        Type = type;
        ExpectedAmountMin = expectedAmountMin;
        ExpectedAmountMax = expectedAmountMax;
        ExpectedRegEx = expectedRegEx;
        Company = company;
        Description = description;
        Category = category;
        NeedAttachment = needAttachment;
        NeedYieldCompanyMatch = needYieldCompanyMatch;
    }

    public string RuleName { get; init; }
    public MatchRuleType Type { get; init; }
    public decimal ExpectedAmountMin { get; init; }
    public decimal ExpectedAmountMax { get; init; }
    public string[] ExpectedRegEx { get; init; }
    public string? Company { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public bool NeedAttachment { get; init; }
    public bool NeedYieldCompanyMatch { get; init; }

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