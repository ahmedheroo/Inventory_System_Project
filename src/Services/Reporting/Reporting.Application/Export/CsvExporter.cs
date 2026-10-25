using System.Text;

namespace InventorySystem.Reports.Application.Export;

/// <summary>
/// Writes an <see cref="ExportDocument"/> as RFC 4180 CSV.
///
/// Two details make the difference between a file a person can open and one they
/// cannot: the UTF-8 BOM (without it Excel guesses the code page and mangles
/// non-ASCII names) and quoting every field that contains a delimiter, a quote or
/// a line break. Timestamps are written in a sortable, unambiguous form.
///
/// The summary travels with the data — after a blank line, as label/value rows —
/// so a downloaded report is complete on its own.
/// </summary>
public static class CsvExporter
{
    public static byte[] Write(ExportDocument document)
    {
        var builder = new StringBuilder();

        for (var tableIndex = 0; tableIndex < document.Tables.Count; tableIndex++)
        {
            if (tableIndex > 0)
            {
                builder.Append("\r\n");
            }

            var table = document.Tables[tableIndex];

            AppendRow(builder, table.Columns.Select(column => column.Header));

            foreach (var row in table.Rows)
            {
                AppendRow(builder, table.Columns.Select((_, index) => ExportValue.Text(row, index)));
            }
        }

        if (document.Summary.Count > 0)
        {
            builder.Append("\r\n");
            AppendRow(builder, ["Summary", ""]);

            foreach (var summaryRow in document.Summary)
            {
                AppendRow(builder, [summaryRow.Label, summaryRow.Value]);
            }
        }

        // UTF-8 with a BOM: Excel reads the encoding from it.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string> fields) =>
        builder.Append(string.Join(",", fields.Select(Escape))).Append("\r\n");

    private static string Escape(string field)
    {
        if (field.Length == 0)
        {
            return string.Empty;
        }

        var needsQuotes = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
                          || field[0] == ' ' || field[^1] == ' ';

        return needsQuotes ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
