using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Logic;

[DebuggerDisplay("{Company}: {Description}]")]
public class FinancialRecord(
    BankEntry bankEntry,
    MatchResult[] matchResults,
    string? company,
    string? description,
    string? category)
{
    public BankEntry BankEntry { get; } = bankEntry;
    public MatchResult[] MatchResults { get; } = matchResults;
    public string? Company { get; set; } = company;
    public string? Description { get; set; } = description;
    public string? Category { get; set; } = category;
    public string? PotentialAttachment { get; set; }

    public override string ToString()
    {
        StringBuilder builder = new();
        builder.AppendLine("<FinancialRecord>");
        builder.AppendLine($"<Date>{BankEntry.Date.ToString("yyyyMMdd")}</Date");
        builder.AppendLine($"<Month{BankEntry.Date.Month}</Month");
        builder.AppendLine($"<Amount>{Math.Abs(BankEntry.Amount).ToString(CultureInfo.InvariantCulture)} DKK</Amount");
        builder.AppendLine($"<RawText>{BankEntry.Text}</RawText");
        if (!string.IsNullOrWhiteSpace(Company))
        {
            builder.AppendLine($"<Issuer>{Company}</Issuer");
        }

        if (!string.IsNullOrWhiteSpace(Description))
        {
            builder.AppendLine($"<Description>{Description}</Description");
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            builder.AppendLine($"<Category>{Category}</Category");
        }

        builder.AppendLine("</FinancialRecord>");

        return builder.ToString();
    }
}