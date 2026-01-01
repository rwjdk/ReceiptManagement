namespace Logic.Models;

public record YearFolder(int Year, string FolderPath, Data Data)
{
    public string DataPath => Path.Combine(FolderPath, "Data.json");
    public string UnprocessedReceiptsFolder => Path.Combine(FolderPath, "Unprocessed Receipts");
    public string ProcessedReceiptsFolder => Path.Combine(FolderPath, "Processed Receipts");
};