namespace InventorySystem.Reports.Application.Export;

/// <summary>
/// The shape of a report when it is handed to a person instead of to a program:
/// one or more tables plus the totals that were shown above them on the screen.
///
/// The model is deliberately format-neutral — the CSV and the Excel writer both
/// consume it — and it carries column *kinds*, because a spreadsheet whose
/// quantity and value columns arrive as text is not worth exporting.
/// </summary>
public sealed record ExportDocument(
    string Title,
    IReadOnlyList<ExportTable> Tables,
    IReadOnlyList<ExportSummaryRow> Summary);

/// <summary>A named table: a header row and its data rows.</summary>
public sealed record ExportTable(
    string Name,
    IReadOnlyList<ExportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record ExportColumn(string Header, ExportColumnKind Kind = ExportColumnKind.Text);

/// <summary>How a cell is written: as text, as a number, or as an ISO timestamp.</summary>
public enum ExportColumnKind
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    DateTime = 3
}

public sealed record ExportSummaryRow(string Label, string Value);

/// <summary>The file format an export was asked for.</summary>
public enum ExportFormat
{
    /// <summary>RFC 4180 CSV, UTF-8 with a BOM so Excel detects the encoding.</summary>
    Csv = 0,

    /// <summary>A real .xlsx workbook (Open XML), one sheet per table plus a summary sheet.</summary>
    Xlsx = 1
}

/// <summary>One definition of how a value is rendered, shared by both writers.</summary>
public static class ExportValue
{
    public const string NullPlaceholder = "";

    /// <summary>The text form of a cell (CSV, strings and timestamps in Excel).</summary>
    public static string Text(IReadOnlyList<object?> row, int index)
    {
        if (index >= row.Count || row[index] is null)
        {
            return NullPlaceholder;
        }

        return row[index] switch
        {
            string text => text,
            bool flag => flag ? "yes" : "no",
            decimal number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            double number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => row[index]!.ToString() ?? NullPlaceholder
        };
    }

    /// <summary>The numeric form of a cell, or null when it is not a number.</summary>
    public static string? Number(IReadOnlyList<object?> row, int index)
    {
        if (index >= row.Count || row[index] is null)
        {
            return null;
        }

        return row[index] switch
        {
            decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
