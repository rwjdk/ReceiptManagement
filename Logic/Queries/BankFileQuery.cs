using System.Globalization;

namespace Logic.Queries;

public class BankFileQuery
{
    public BankEntry[] ReadEntries(string path)
    {
        List<BankEntry> result = [];
        string[] lines = File.ReadAllLines(path).Skip(1).ToArray();
        foreach (string line in lines)
        {
            string[] parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
            DateOnly date = DateOnly.ParseExact(parts[0], "yyyy/MM/dd");
            decimal amount = decimal.Parse(parts[1], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            string text = parts[3];
            result.Add(new BankEntry(date, text, amount));
        }

        return result.ToArray();
    }
}