using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using Microsoft.Agents.AI;

namespace Logic;

public class AccountQuery(AzureOpenAIAgentFactory agentFactory)
{
    public Account[] GetAccounts(string rootFolder)
    {
        string[] accountFiles = Directory.GetFiles(rootFolder, $"*.{Account.Extension}");
        return accountFiles.Select(x => JsonSerializer.Deserialize<Account>(File.ReadAllText(x))!).ToArray(); //todo - safety - if you can deserialize
    }
}