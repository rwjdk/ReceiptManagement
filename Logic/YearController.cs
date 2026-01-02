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

        foreach (Account account in year.Data.Accounts)
        {
            //Create Final CSV-Files
            StringBuilder csvBuilder = new();
            csvBuilder.AppendLine("Dato;Tekst;Beløb;Firma;Beskrivelse;Kategori;Bilag;Balance");
            foreach (AccountRecord record in account.Records.OrderBy(x => x.LineNum))
            {
                csvBuilder.AppendLine($"{record.BankEntry.Date.ToString("dd-MM-yyyy")};" +
                                      $"\"{record.BankEntry.Text.Replace("\"", "\"\"")}\";" +
                                      $"{record.BankEntry.Amount:N2};" +
                                      $"\"{record.Company?.Replace("\"", "\"\"")}\";" +
                                      $"\"{record.Description?.Replace("\"", "\"\"")}\";" +
                                      $"\"{record.Category?.Replace("\"", "\"\"")}\";" +
                                      $"\"{record.Attachment?.Replace("\"", "\"\"") ?? ""}\";" +
                                      $"{record.BankEntry.Balance:N2}"
                );
            }

            File.WriteAllText(Path.Combine(year.FolderPath, account.Name + ".csv"), csvBuilder.ToString(), Encoding.UTF8);
        }
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