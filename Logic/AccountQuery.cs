using System.Text.Json;

namespace Logic;

public class AccountQuery()
{
    public List<Account> GetAccounts(string rootFolder)
    {
        string[] accountFiles = Directory.GetFiles(rootFolder, $"*.{Account.Extension}", SearchOption.AllDirectories);
        return accountFiles.Select(x => JsonSerializer.Deserialize<Account>(File.ReadAllText(x))!).ToList(); //todo - safety - if you can deserialize
    }

    public string GetAccountFolder(string rootFolder, Account account)
    {
        string accountsFolder = GetAccountsFolder(rootFolder);
        string accountFolder = Path.Combine(accountsFolder, account.GetFolderName());
        if (!Directory.Exists(accountFolder))
        {
            Directory.CreateDirectory(accountFolder);
        }

        return accountFolder;
    }

    private string GetAccountsFolder(string rootFolder)
    {
        string accountFolder = Path.Combine(rootFolder, "Accounts");
        if (!Directory.Exists(accountFolder))
        {
            Directory.CreateDirectory(accountFolder);
        }

        return accountFolder;
    }
}