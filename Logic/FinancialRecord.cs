using System.Diagnostics;
using Logic.Queries;

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
    public string? Company { get; init; } = company;
    public string? Description { get; set; } = description;
    public string? Category { get; set; } = category;
    public string? PotentialAttachment { get; set; }
}