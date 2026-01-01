using System.Text.Json.Serialization;

namespace Logic.Models;

public class Account
{
    public required string Name { get; set; }
    public required List<AccountRecord> Records { get; set; }

    [JsonIgnore]
    public string DisplayName => $"{Name}";

    [JsonIgnore]
    public string RecordsDisplayName => $"{Records.Count} Records [{Records.Count(x => !x.Confirmed)} Unconfirmed | {Records.Count(x => !x.IsComplete)} Incomplete]";
}