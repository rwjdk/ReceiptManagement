using Logic.Models;
using System.Text;
using System.Text.Json;

namespace Logic;

public class YearController
{
    public Data GetOrCreate(string path, string[] accountNames)
    {
        if (!File.Exists(path))
        {
            List<Account> accounts = [];
            foreach (string accountName in accountNames)
            {
                Account account = new()
                {
                    Name = accountName,
                    Records = []
                };
                accounts.Add(account);
            }

            Data yearData = new Data
            {
                Accounts = accounts
            };

            File.WriteAllText(path, JsonSerializer.Serialize(yearData), new UTF8Encoding());
            return yearData;
        }

        string json = File.ReadAllText(path, new UTF8Encoding());
        return JsonSerializer.Deserialize<Data>(json)!;
    }

    public void Update(YearFolder year)
    {
        CreateBackup(year.DataPath);
        File.WriteAllText(year.DataPath, JsonSerializer.Serialize(year.Data), Encoding.UTF8);
    }

    private static void CreateBackup(string path)
    {
        if (!File.Exists(path)) //First time (no file, so nothing to back up)
        {
            return;
        }

        string backupFileName = $"{Path.GetFileNameWithoutExtension(path)}_backup_{DateTime.Today:yyyyMMdd}{Path.GetExtension(path)}";
        string backupPath = Path.Combine(Path.GetDirectoryName(path)!, backupFileName);
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }
    }
}