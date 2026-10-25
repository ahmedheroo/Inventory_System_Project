using System.IO.Compression;
using System.Text;
using System.Xml;

namespace InventorySystem.Reports.Application.Export;

/// <summary>
/// Writes an <see cref="ExportDocument"/> as a real .xlsx workbook (Open XML), so
/// "export to Excel" means a file with sheets, numbers that stay numbers and a
/// summary sheet that can be summed — not a CSV with a different extension.
///
/// The package is assembled directly (the Open XML format is a zip of small XML
/// parts), which keeps the Reporting service free of a spreadsheet dependency: the
/// parts written here are the content types, the package and workbook
/// relationships, one worksheet per table and, when there is a summary, one
/// summary sheet. Cell styles are omitted deliberately — an unformatted but valid
/// workbook opens everywhere, and styling is the reader's job.
/// </summary>
public static class XlsxExporter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const int MaxSheetNameLength = 31;

    public static byte[] Write(ExportDocument document)
    {
        var sheets = BuildSheets(document);

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Count));
            AddEntry(archive, "_rels/.rels", PackageRelationships());
            AddEntry(archive, "xl/workbook.xml", Workbook(sheets));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));

            for (var index = 0; index < sheets.Count; index++)
            {
                AddEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheets[index]));
            }
        }

        return buffer.ToArray();
    }

    private static List<Sheet> BuildSheets(ExportDocument document)
    {
        var sheets = new List<Sheet>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in document.Tables)
        {
            sheets.Add(new Sheet(UniqueName(table.Name, usedNames), table.Columns, table.Rows));
        }

        if (document.Summary.Count > 0)
        {
            var columns = new[] { new ExportColumn("Measure"), new ExportColumn("Value") };

            var rows = document.Summary
                .Select(summary => (IReadOnlyList<object?>)new object?[] { summary.Label, summary.Value })
                .ToList();

            sheets.Add(new Sheet(UniqueName("Summary", usedNames), columns, rows));
        }

        return sheets;
    }

    private static string UniqueName(string name, ISet<string> usedNames)
    {
        var cleaned = new string(name.Where(character => !"[]:*?/\\".Contains(character)).ToArray()).Trim();

        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }

        if (cleaned.Length > MaxSheetNameLength)
        {
            cleaned = cleaned[..MaxSheetNameLength];
        }

        var candidate = cleaned;
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            var tail = $" ({suffix++})";
            candidate = cleaned.Length + tail.Length > MaxSheetNameLength
                ? cleaned[..(MaxSheetNameLength - tail.Length)] + tail
                : cleaned + tail;
        }

        return candidate;
    }

    private static string Worksheet(Sheet sheet)
    {
        var xml = new StringBuilder();

        xml.Append(XmlDeclaration);
        xml.Append($"<worksheet xmlns=\"{SpreadsheetNamespace}\">");

        if (sheet.Columns.Count > 0)
        {
            xml.Append("<cols>");

            for (var index = 0; index < sheet.Columns.Count; index++)
            {
                xml.Append($"<col min=\"{index + 1}\" max=\"{index + 1}\" width=\"{WidthOf(sheet.Columns[index].Kind)}\" customWidth=\"1\"/>");
            }

            xml.Append("</cols>");
        }

        xml.Append("<sheetData>");
        xml.Append(Row(1, sheet.Columns.Select(column => CellValue.Text(column.Header))));

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var row = sheet.Rows[index];

            xml.Append(Row(
                index + 2,
                sheet.Columns.Select((column, columnIndex) => Cell(column.Kind, row, columnIndex))));
        }

        xml.Append("</sheetData></worksheet>");

        return xml.ToString();
    }

    private static CellValue Cell(ExportColumnKind kind, IReadOnlyList<object?> row, int index)
    {
        if (kind is ExportColumnKind.Integer or ExportColumnKind.Decimal
            && ExportValue.Number(row, index) is { } number)
        {
            return CellValue.Number(number);
        }

        return CellValue.Text(ExportValue.Text(row, index));
    }

    private static string Row(int rowNumber, IEnumerable<CellValue> cells)
    {
        var xml = new StringBuilder($"<row r=\"{rowNumber}\">");

        var columnIndex = 0;

        foreach (var cell in cells)
        {
            var reference = $"{ColumnName(columnIndex)}{rowNumber}";

            xml.Append(cell.IsNumber
                ? $"<c r=\"{reference}\"><v>{cell.Value}</v></c>"
                : $"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(cell.Value)}</t></is></c>");

            columnIndex++;
        }

        return xml.Append("</row>").ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;

        for (var remaining = index; remaining >= 0; remaining = remaining / 26 - 1)
        {
            name = (char)('A' + remaining % 26) + name;
        }

        return name;
    }

    private static string WidthOf(ExportColumnKind kind) => kind switch
    {
        ExportColumnKind.Integer => "12",
        ExportColumnKind.Decimal => "14",
        ExportColumnKind.DateTime => "20",
        _ => "22"
    };

    private static string ContentTypes(int sheetCount)
    {
        var xml = new StringBuilder();

        xml.Append(XmlDeclaration);
        xml.Append($"<Types xmlns=\"{ContentTypesNamespace}\">");
        xml.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        xml.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        xml.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

        for (var index = 1; index <= sheetCount; index++)
        {
            xml.Append($"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return xml.Append("</Types>").ToString();
    }

    private static string PackageRelationships()
    {
        var xml = new StringBuilder();

        xml.Append(XmlDeclaration);
        xml.Append($"<Relationships xmlns=\"{PackageRelationshipNamespace}\">");
        xml.Append($"<Relationship Id=\"rId1\" Type=\"{RelationshipNamespace}/officeDocument\" Target=\"xl/workbook.xml\"/>");

        return xml.Append("</Relationships>").ToString();
    }

    private static string Workbook(IReadOnlyList<Sheet> sheets)
    {
        var xml = new StringBuilder();

        xml.Append(XmlDeclaration);
        xml.Append($"<workbook xmlns=\"{SpreadsheetNamespace}\" xmlns:r=\"{RelationshipNamespace}\"><sheets>");

        for (var index = 0; index < sheets.Count; index++)
        {
            xml.Append($"<sheet name=\"{Escape(sheets[index].Name)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>");
        }

        return xml.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRelationships(int sheetCount)
    {
        var xml = new StringBuilder();

        xml.Append(XmlDeclaration);
        xml.Append($"<Relationships xmlns=\"{PackageRelationshipNamespace}\">");

        for (var index = 1; index <= sheetCount; index++)
        {
            xml.Append($"<Relationship Id=\"rId{index}\" Type=\"{RelationshipNamespace}/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>");
        }

        return xml.Append("</Relationships>").ToString();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.Write(content);
    }

    /// <summary>Escapes text for XML and drops characters XML 1.0 cannot carry.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&apos;");
                    break;
                default:
                    if (XmlConvert.IsXmlChar(character))
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private sealed record Sheet(string Name, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

    private readonly record struct CellValue(string Value, bool IsNumber)
    {
        public static CellValue Text(string value) => new(value, false);

        public static CellValue Number(string value) => new(value, true);
    }
}
