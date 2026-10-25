using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace InventorySystem.Reports.Api.Tests;

/// <summary>
/// The downloads: every report as CSV and as an Excel workbook. The assertions are
/// about the things a person would notice — the file opens, the
/// numbers are numbers, the rows are the filtered ones and not just the first
/// page, and the text survives a comma.
/// </summary>
public class ReportExportTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static void SeedCatalogue(ReportingApiFactory factory) =>
        factory.Catalog
            .WithProduct(1, "SKU-1", "Widget", unitPrice: 10m, minimumStockLevel: 5, categoryId: 1)
            .WithProduct(2, "SKU-2", "Gadget", unitPrice: 2.5m, minimumStockLevel: 1, categoryId: 2)
            .WithWarehouse(1, "WH-CAI", "Cairo Main")
            .WithWarehouse(2, "WH-ALX", "Alexandria");

    private static async Task GivenStockAsync(
        ReportingApiFactory factory,
        int productId,
        int warehouseId,
        int quantity,
        int minute,
        int minimumStockLevel = 5) =>
        await factory.ProjectAsync(TestEvents.Received(
            productId,
            warehouseId,
            quantity,
            quantity,
            TestEvents.At(minute),
            minimumStockLevel));

    private static async Task<(byte[] Bytes, string ContentType, string FileName, HttpStatusCode Status)> DownloadAsync(
        HttpClient client,
        string url)
    {
        var response = await client.GetAsync(url, Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        return (
            bytes,
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty,
            response.StatusCode);
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    // -----------------------------------------------------------------------
    // CSV
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_inventory_export_is_csv_with_a_bom_and_the_filtered_rows()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 10, 0);
        await GivenStockAsync(factory, 2, 2, 4, 1, minimumStockLevel: 1);
        await GivenStockAsync(factory, 2, 1, 7, 2, minimumStockLevel: 1);

        var download = await DownloadAsync(client, "/api/reports/inventory/export?warehouseId=1");

        Assert.Equal(HttpStatusCode.OK, download.Status);
        Assert.Equal("text/csv", download.ContentType);
        Assert.StartsWith("inventory-report-", download.FileName);
        Assert.EndsWith(".csv", download.FileName);

        // UTF-8 BOM: without it Excel guesses the code page.
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, download.Bytes.Take(3));

        var csv = Text(download.Bytes);
        var lines = csv.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "Product id,Sku,Product,Warehouse id,Warehouse code,Warehouse,Quantity,Minimum,Unit price,Value,Low stock,Out of stock,Updated at",
            lines[0]);

        // warehouseId=1 keeps two of the three stock levels, and the summary follows.
        Assert.Equal(2, lines.Count(line => line.StartsWith("1,SKU-1,") || line.StartsWith("2,SKU-2,")));
        Assert.DoesNotContain("WH-ALX", csv);
        Assert.Contains("Summary", csv);
        Assert.Contains("Total quantity,17", csv);            // 10 + 7 in warehouse 1
        Assert.Contains("Total value,117.5", csv);            // 100 + 17.5
    }

    [Fact]
    public async Task The_csv_quotes_values_that_contain_delimiters_and_quotes()
    {
        using var factory = new ReportingApiFactory();
        factory.Catalog
            .WithProduct(1, "SKU-1", "Widget, 12\" wide", unitPrice: 10m, minimumStockLevel: 5)
            .WithWarehouse(1, "WH-CAI", "Cairo \"Main\"");

        var client = factory.CreateClient();
        await GivenStockAsync(factory, 1, 1, 10, 0);

        var csv = Text((await DownloadAsync(client, "/api/reports/inventory/export")).Bytes)
            .TrimStart('\uFEFF');

        Assert.Contains("\"Widget, 12\"\" wide\"", csv);
        Assert.Contains("\"Cairo \"\"Main\"\"\"", csv);
    }

    [Fact]
    public async Task An_export_is_not_limited_to_one_page()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        for (var product = 1; product <= 2; product++)
        {
            for (var warehouse = 1; warehouse <= 2; warehouse++)
            {
                await GivenStockAsync(factory, product, warehouse, 5, product * 2 + warehouse);
            }
        }

        // The report endpoint would return one row for this page size...
        var report = await client.GetStringAsync("/api/reports/inventory?page=1&pageSize=1", Ct);
        Assert.Contains("\"totalCount\":4", report);

        // ...the download still carries every matching row plus the summary.
        var csv = Text((await DownloadAsync(client, "/api/reports/inventory/export?pageSize=1")).Bytes)
            .TrimStart('\uFEFF');

        var dataRows = csv.Split("\r\n").Where(line => line.StartsWith("1,SKU-1,") || line.StartsWith("2,SKU-2,")).ToList();

        Assert.Equal(4, dataRows.Count);
        Assert.Contains("Stock levels,4", csv);
    }

    [Fact]
    public async Task The_movement_export_contains_the_whole_filtered_ledger_and_its_summary()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 10, 0);
        await factory.ProjectAsync(TestEvents.Issued(1, 1, 4, 6, TestEvents.At(1)));
        await factory.ProjectAsync(TestEvents.Transferred(1, 1, 2, 2, 4, 2, TestEvents.At(2)));

        var csv = Text((await DownloadAsync(client, "/api/reports/movement/export?pageSize=1")).Bytes)
            .TrimStart('\uFEFF');

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Id,Occurred at,Product id,Sku,Product,Warehouse id,Warehouse code,Warehouse,Type,Quantity,Signed quantity,Quantity after", lines[0]);
        Assert.Equal(4, lines.Count(line => line.Contains("SKU-1,")));
        Assert.Contains("TRANSFER_IN", csv);
        Assert.Contains("TRANSFER_OUT", csv);
        Assert.Contains("Movements,4", csv);
        Assert.Contains("Net change,6", csv);                   // +10 +2 -4 -2
    }

    [Fact]
    public async Task The_low_stock_export_respects_its_filters()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 3, 0);                        // low, shortfall 2
        await factory.ProjectAsync(TestEvents.Issued(1, 2, 3, 0, TestEvents.At(1)));   // out of stock, shortfall 5

        var everything = Text((await DownloadAsync(client, "/api/reports/low-stock/export")).Bytes)
            .TrimStart('\uFEFF');

        var existingOnly = Text((await DownloadAsync(client, "/api/reports/low-stock/export?includeOutOfStock=false")).Bytes)
            .TrimStart('\uFEFF');

        Assert.Contains("OUT_OF_STOCK", everything);
        Assert.Contains("LOW_STOCK", everything);
        Assert.Contains("Total shortfall,7", everything);

        Assert.Contains("LOW_STOCK", existingOnly);
        Assert.DoesNotContain("OUT_OF_STOCK", existingOnly.Split("Summary")[0]);
        Assert.Contains("Total shortfall,2", existingOnly);
    }

    [Fact]
    public async Task The_valuation_export_reports_value_as_a_number()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 5, 0);

        var download = await DownloadAsync(client, "/api/reports/valuation/export?format=csv");

        Assert.Equal("text/csv", download.ContentType);
        Assert.StartsWith("valuation-report-", download.FileName);

        var csv = Text(download.Bytes).TrimStart('\uFEFF');

        Assert.Contains("5,10,50", csv);
        Assert.Contains("Total value,50", csv);
    }

    // -----------------------------------------------------------------------
    // Excel
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_excel_export_is_a_valid_workbook_whose_numbers_are_numbers()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 10, 0);

        var download = await DownloadAsync(client, "/api/reports/inventory/export?format=xlsx");

        Assert.Equal(HttpStatusCode.OK, download.Status);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            download.ContentType);
        Assert.EndsWith(".xlsx", download.FileName);

        using var archive = new ZipArchive(new MemoryStream(download.Bytes), ZipArchiveMode.Read);

        var names = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
        Assert.Contains("xl/worksheets/sheet2.xml", names);      // the summary sheet

        // Every part has to be well-formed XML or Excel refuses the file.
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            var read = XDocument.Load(stream);

            Assert.NotNull(read.Root);
        }

        var sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        var workbook = ReadEntry(archive, "xl/workbook.xml");

        Assert.Contains("Inventory", workbook);
        Assert.Contains("Summary", workbook);
        Assert.Contains("Product id", sheet);
        Assert.Contains("SKU-1", sheet);

        // The quantity is a numeric cell (no t="inlineStr"), so a spreadsheet can sum it.
        Assert.Contains("<c r=\"G2\"><v>10</v></c>", sheet);
        Assert.Contains("<c r=\"J2\"><v>100</v></c>", sheet);
        Assert.Contains("<c r=\"B2\" t=\"inlineStr\">", sheet);

        Assert.Contains("Total value", ReadEntry(archive, "xl/worksheets/sheet2.xml"));
    }

    [Fact]
    public async Task The_valuation_workbook_has_one_sheet_per_table()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 5, 0);

        var download = await DownloadAsync(client, "/api/reports/valuation/export?format=xlsx");

        using var archive = new ZipArchive(new MemoryStream(download.Bytes), ZipArchiveMode.Read);

        // Valuation + By warehouse + Summary.
        Assert.Equal(3, archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet")));
        Assert.Contains("By warehouse", ReadEntry(archive, "xl/workbook.xml"));
        Assert.Contains("Warehouse code", ReadEntry(archive, "xl/worksheets/sheet2.xml"));
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    // -----------------------------------------------------------------------
    // Failure cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_export_format_is_rejected()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reports/inventory/export?format=pdf", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_movement_export_rejects_a_reversed_date_range()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        var from = Uri.EscapeDataString(TestEvents.At(3).ToString("O"));
        var to = Uri.EscapeDataString(TestEvents.At(1).ToString("O"));

        var response = await client.GetAsync($"/api/reports/movement/export?from={from}&to={to}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'from'", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_export_reports_503_when_the_product_service_is_unreachable()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenStockAsync(factory, 1, 1, 10, 0);

        factory.Catalog.Unavailable = true;

        var response = await client.GetAsync("/api/reports/valuation/export?format=xlsx", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
