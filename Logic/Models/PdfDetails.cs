using System.Globalization;
using System.Text;
using JetBrains.Annotations;

namespace Logic.Models;

[UsedImplicitly]
internal class PdfDetails
{
    public string? Issuer { get; set; }
    public DateTime? Date { get; set; }
    public int Month { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    public string ToString(string rawData)
    {
        StringBuilder builder = new();
        builder.AppendLine("<PdfContent>");
        builder.AppendLine($"<Issuer>{Issuer ?? "???"}</Issuer>");
        builder.AppendLine($"<Date>{Date?.ToString("yyyyMMdd") ?? "???"}</Date>");
        builder.AppendLine($"<Month>{Month}</Date>");
        builder.AppendLine($"<Amount>{Amount?.ToString(CultureInfo.InvariantCulture) ?? "???"} {Currency}</Amount>");
        builder.AppendLine($"<RawData>{rawData}</RawData>");
        builder.AppendLine("</PdfContent>");

        return builder.ToString();
    }
}