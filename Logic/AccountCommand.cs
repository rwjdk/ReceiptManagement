using System.Text;
using System.Text.Json;

namespace Logic;

public class AccountCommand(AccountQuery accountQuery)
{
    public void CreateAccount(string rootFolder, int year, string accountName)
    {
        Account account = new Account
        {
            Year = year,
            Name = accountName,
            Records = []
        };
        File.WriteAllText(Path.Combine(rootFolder, account.GetFileName()), JsonSerializer.Serialize(account));
    }

    public void UpdateAccount(string rootFolder, Account account)
    {
        string path = Path.Combine(rootFolder, account.GetFileName());
        CreateBackup(path);
        File.WriteAllText(path, JsonSerializer.Serialize(account), Encoding.UTF8);
    }

    private static void CreateBackup(string path)
    {
        if (!File.Exists(path)) //First time (no file, so nothing to back up)
        {
            return;
        }

        string backupFileName = $"{Path.GetFileNameWithoutExtension(path)}_backup{DateTime.Today:yyyyMMdd}.{Path.GetExtension(path)}";
        string backupPath = Path.Combine(Path.GetDirectoryName(path)!, backupFileName);
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }
    }
}