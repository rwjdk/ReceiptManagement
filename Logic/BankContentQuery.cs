using System.Globalization;

namespace Logic;

public class BankContentQuery
{
    public BankEntry[] ReadEntries(string content, Account account)
    {
        List<BankEntry> result = [];
        string[] lines = content.Split('\n');
        foreach (string line in lines.Skip(1))
        {
            string[] parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
            DateOnly date = DateOnly.ParseExact(parts[0], "yyyy/MM/dd");
            decimal amount = decimal.Parse(parts[1], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            string text = parts[3];
            decimal balance = decimal.Parse(parts[4], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            result.Add(new BankEntry(date, text, amount, balance));
        }

        return result.ToArray();
    }
}