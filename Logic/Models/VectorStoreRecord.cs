using JetBrains.Annotations;
using Microsoft.Extensions.VectorData;

namespace Logic.Models;

[UsedImplicitly]
public class VectorStoreRecord
{
    [VectorStoreKey]
    public required string Id { get; set; }

    [VectorStoreData]
    public string? Issuer { get; set; }

    [VectorStoreData]
    public string? Date { get; set; }

    [VectorStoreData]
    public int Month { get; set; }

    [VectorStoreData]
    public int? Amount { get; set; }

    [VectorStoreData]
    public required string Content { get; set; }

    [VectorStoreData]
    public required string FileName { get; set; }

    [VectorStoreVector(1536)]
    [UsedImplicitly]
    public string Vector => Content;

    public override string ToString()
    {
        return $"<pdf filename=\"{FileName}\">{Content}</pdf>";
    }
}