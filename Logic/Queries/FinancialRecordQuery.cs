using System.Text.Json;

namespace Logic.Queries;

public class FinancialRecordQuery
{
    public FinancialRecord[] GetExisting(int year, string account)
    {
        string path = $"{account}-{year}.json";

        if (!File.Exists(path))
        {
            return [];
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FinancialRecord[]>(json)!;
    }

    public FinancialRecord[] FromBankEntries(BankEntry[] bankEntries, MatchRule[] matchRules)
    {
        List<FinancialRecord> result = [];
        foreach (BankEntry bankEntry in bankEntries)
        {
            MatchResult[] matchResults = matchRules.Where(x => x.Match(bankEntry) != null).Select(x => x.Match(bankEntry)!).ToArray();
            switch (matchResults.Length)
            {
                case 0:
                    result.Add(new FinancialRecord(false, bankEntry.Date, bankEntry.Text, bankEntry.Amount, "TODO", "TODO", "TODO", "TODO"));
                    break;
                case 1:
                    MatchResult match = matchResults[0];
                    result.Add(new FinancialRecord(true, bankEntry.Date, bankEntry.Text, bankEntry.Amount, match.Company, match.Description, match.Category, match.NeedAttachment ? "TODO" : string.Empty));
                    break;
                default:
                    result.Add(new FinancialRecord(false, bankEntry.Date, bankEntry.Text, bankEntry.Amount, "TODO (Multiple Matches)", "TODO", "TODO", "TODO"));
                    break;
            }
        }

        return result.ToArray();
    }
}