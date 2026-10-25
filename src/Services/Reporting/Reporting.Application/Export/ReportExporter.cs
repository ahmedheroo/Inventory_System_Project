namespace InventorySystem.Reports.Application.Export;

/// <summary>A rendered export, ready to be returned as a download.</summary>
public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Renders an <see cref="ExportDocument"/> in the requested format. The content
/// types are the ones a browser and Excel recognise, and the file name carries the
/// generation date so two downloads do not overwrite each other in a downloads
/// folder.
/// </summary>
public static class ReportExporter
{
    public const string CsvContentType = "text/csv";

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <param name="name">The report's file name stem, e.g. <c>inventory-report</c>.</param>
    public static ExportedFile ToFile(
        ExportDocument document,
        ExportFormat format,
        string name,
        DateTime generatedAt)
    {
        var stamp = generatedAt.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);

        return format switch
        {
            ExportFormat.Xlsx => new ExportedFile(
                $"{name}-{stamp}.xlsx",
                XlsxContentType,
                XlsxExporter.Write(document)),

            _ => new ExportedFile(
                $"{name}-{stamp}.csv",
                CsvContentType,
                CsvExporter.Write(document))
        };
    }
}
