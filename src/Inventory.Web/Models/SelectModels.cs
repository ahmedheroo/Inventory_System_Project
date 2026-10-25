using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>One choice in a select, already written the way it should read on screen.</summary>
public sealed record PickOption(int Value, string Label)
{
    public static PickOption Of(int value, params string?[] parts) =>
        new(value, string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part))));
}

/// <summary>
/// A select on a filter bar, a form or an operation page. One shape for all of them
/// keeps the keyboard behaviour and the markup identical wherever a person picks a
/// product, a warehouse or a transaction type.
/// </summary>
public sealed class SelectModel
{
    public required string FieldName { get; init; }

    public required string Label { get; init; }

    public IReadOnlyList<PickOption> Options { get; init; } = [];

    public int? Selected { get; init; }

    /// <summary>What the empty choice says ("All products"); null when a choice is required.</summary>
    public string? EmptyLabel { get; init; }

    public bool Required => EmptyLabel is null;

    public string? Hint { get; init; }

    public string Id => "f-" + FieldName;
}

/// <summary>
/// The pickers the console offers, built from the catalogue it already loaded. A page
/// never asks a service for a dropdown it can build from the catalogue read it needed
/// anyway.
/// </summary>
public static class Pickers
{
    public static SelectModel Products(
        CatalogLookup catalog,
        string fieldName = "ProductId",
        string label = "Product",
        int? selected = null,
        string? emptyLabel = "All products") =>
        new()
        {
            FieldName = fieldName,
            Label = label,
            Selected = selected,
            EmptyLabel = emptyLabel,
            Options = catalog.ActiveProducts
                .Select(product => PickOption.Of(product.Id, product.Sku, product.Name))
                .ToList()
        };

    public static SelectModel Warehouses(
        CatalogLookup catalog,
        string fieldName = "WarehouseId",
        string label = "Warehouse",
        int? selected = null,
        string? emptyLabel = "All warehouses") =>
        new()
        {
            FieldName = fieldName,
            Label = label,
            Selected = selected,
            EmptyLabel = emptyLabel,
            Options = catalog.ActiveWarehouses
                .Select(warehouse => PickOption.Of(warehouse.Id, warehouse.Code, warehouse.Name))
                .ToList()
        };

    public static SelectModel Categories(
        IReadOnlyList<CategoryDto> categories,
        int? selected = null,
        string emptyLabel = "All categories") =>
        new()
        {
            FieldName = "CategoryId",
            Label = "Category",
            Selected = selected,
            EmptyLabel = emptyLabel,
            Options = categories.Select(category => new PickOption(category.Id, category.Name)).ToList()
        };

    public static readonly IReadOnlyList<PickOption> MovementTypes =
    [
        new((int)StockTransactionType.RECEIVE, "Received"),
        new((int)StockTransactionType.ISSUE, "Issued"),
        new((int)StockTransactionType.TRANSFER_IN, "Transfer in"),
        new((int)StockTransactionType.TRANSFER_OUT, "Transfer out"),
        new((int)StockTransactionType.ADJUSTMENT, "Adjusted")
    ];

    public static readonly IReadOnlyList<PickOption> AlertTypes =
    [
        new((int)AlertType.LOW_STOCK, "Low stock"),
        new((int)AlertType.OUT_OF_STOCK, "Out of stock")
    ];

    public static readonly IReadOnlyList<PickOption> AlertStatuses =
    [
        new((int)AlertStatus.ACTIVE, "Active"),
        new((int)AlertStatus.RESOLVED, "Resolved")
    ];

    public static readonly IReadOnlyList<PickOption> ReadStates =
    [
        new(0, "Unread"),
        new(1, "Read")
    ];
}
