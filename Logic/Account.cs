namespace Logic;

public class Account
{
    public const string Extension = "account";

    public required int Year { get; set; }
    public required string Name { get; set; }
    public required List<FinancialRecord> Records { get; set; }
    public string DisplayName => $"{Name} ({Year})";

    public string GetFileName()
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            Name = Name.Replace(c, '_');
        }

        return $"{Name}-{Year}.{Extension}";
    }
}