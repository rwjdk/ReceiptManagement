using System.Text.Json;

namespace Logic;

public class ConfigurationQuery
{
    public Configuration GetConfiguration(string rootFolder)
    {
        string configPath = Path.Combine(rootFolder, "configuration.json");
        string json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<Configuration>(json)!;
    }
}