using System.Text;
using System.Text.Json;

namespace Logic;

public class AccountCommand(AccountQuery accountQuery)
{
    public Account CreateAccount(string rootFolder, int year, string accountName)
    {
        Account account = new()
        {
            Year = year,
            Name = accountName,
            Records = []
        };

        string accountFolder = accountQuery.GetAccountFolder(rootFolder, account);

        File.WriteAllText(Path.Combine(accountFolder, account.GetFileName()), JsonSerializer.Serialize(account));
        return account;
    }

    public void UpdateAccount(string rootFolder, Account account)
    {
        string accountFolder = accountQuery.GetAccountFolder(rootFolder, account);
        string path = Path.Combine(accountFolder, account.GetFileName());
        CreateBackup(path);
        File.WriteAllText(path, JsonSerializer.Serialize(account), Encoding.UTF8);
    }

    private static void CreateBackup(string path)
    {
        if (!File.Exists(path)) //First time (no file, so nothing to back up)
        {
            return;
        }

        string backupFileName = $"{Path.GetFileNameWithoutExtension(path)}_backup{DateTime.Today:yyyyMMdd}{Path.GetExtension(path)}.bak";
        string backupPath = Path.Combine(Path.GetDirectoryName(path)!, backupFileName);
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }
    }
}