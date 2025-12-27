using System.Text;
using System.Text.Json;
using Logic.Queries;

namespace Logic.Commands;

public class FinancialRecordCommand(FinancialRecordQuery financialRecordQuery)
{
    public void Save(int year, string account, IList<FinancialRecord> records)
    {
        string path = financialRecordQuery.GetTarget(year, account);

        CreateBackup(path);
        File.WriteAllText(path, JsonSerializer.Serialize(records), Encoding.UTF8);
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