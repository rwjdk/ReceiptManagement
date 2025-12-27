using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using Microsoft.Agents.AI;

namespace Logic.Queries;

public class FinancialRecordQuery(AzureOpenAIAgentFactory agentFactory)
{
    public List<FinancialRecord> GetExisting(int year, string account)
    {
        string path = GetTarget(year, account);

        if (!File.Exists(path))
        {
            return [];
        }

        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<FinancialRecord>>(json)!;
    }

    public string GetTarget(int year, string account)
    {
        string path = $"{account}-{year}.json";
        return path;
    }

    public async Task<FinancialRecord[]> FromBankEntries(BankEntry[] bankEntries, MatchRule[] matchRules)
    {
        List<FinancialRecord> result = [];
        foreach (BankEntry bankEntry in bankEntries)
        {
            MatchResult[] matchResults = matchRules.Where(x => x.Match(bankEntry) != null).Select(x => x.Match(bankEntry)!).ToArray();
            switch (matchResults.Length)
            {
                case 1:
                    MatchResult match = matchResults[0];
                    result.Add(new FinancialRecord(bankEntry, matchResults, match.Company, match.Description, match.Category));
                    break;
                default:
                    result.Add(new FinancialRecord(bankEntry, matchResults, null, null, null));
                    break;
            }
        }

        return result.ToArray();
    }
}