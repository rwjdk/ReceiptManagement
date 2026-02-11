using System.Globalization;

namespace Logic.Models;

public record BankEntry(DateOnly Date, string Text, decimal Amount, decimal Balance, string AccountNumber)
{
    public string MatchKey()
    {
        return Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Text + Amount.ToString(CultureInfo.InvariantCulture);
    }
}