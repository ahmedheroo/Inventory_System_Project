using Inventory.Web.Api.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace InventorySystem.Web.Tests;

internal static class TestData
{
    public static readonly DateTime When = new(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);

    public static ProductDto Product(
        int id,
        string sku,
        string name,
        decimal unitPrice = 10m,
        int minimumStockLevel = 5,
        bool isActive = true,
        int? categoryId = null,
        string? categoryName = null) =>
        new(id, sku, name, $"{name} in the catalogue", categoryId, categoryName, unitPrice, minimumStockLevel, isActive, When, When);

    public static WarehouseDto Warehouse(int id, string code, string name, bool isActive = true) =>
        new(id, code, name, null, isActive, When, When);

    public static CategoryDto Category(int id, string name, int productCount = 0) =>
        new(id, name, null, productCount, When, When);

    public static AlertDto Alert(
        int id,
        int productId,
        int warehouseId,
        AlertType type = AlertType.LOW_STOCK,
        bool isRead = false,
        AlertStatus status = AlertStatus.ACTIVE,
        int currentQuantity = 3,
        int minimumStockLevel = 5) =>
        new(id, productId, warehouseId, type, $"Product {productId} is short", currentQuantity, minimumStockLevel,
            isRead, status, When, When, isRead ? When : null, status == AlertStatus.RESOLVED ? When : null);

    public static InventoryReportRow StockRow(
        int productId,
        string sku,
        int warehouseId,
        string warehouseCode,
        int quantity,
        int minimum = 5,
        decimal unitPrice = 10m) =>
        new(productId, sku, sku, warehouseId, warehouseCode, warehouseCode, quantity, minimum, unitPrice,
            quantity * unitPrice, quantity <= minimum, quantity == 0, When);

    public static LowStockReportRow ShortRow(
        int productId,
        string sku,
        int warehouseId,
        string warehouseCode,
        int quantity,
        int minimum = 5) =>
        new(productId, sku, sku, warehouseId, warehouseCode, warehouseCode, quantity, minimum,
            Math.Max(minimum - quantity, 0), quantity == 0 ? LowStockStatus.OUT_OF_STOCK : LowStockStatus.LOW_STOCK, When);

    public static MovementReportRow Movement(
        int id,
        int productId,
        int warehouseId,
        StockTransactionType type,
        int signedQuantity,
        int quantityAfter) =>
        new(id, productId, productId.ToString(), $"Product {productId}", warehouseId, $"WH-{warehouseId}", $"Warehouse {warehouseId}",
            type, Math.Abs(signedQuantity), signedQuantity, quantityAfter, When.AddMinutes(-id));

    /// <summary>
    /// A controller with the two things it cannot run without in a test: a temp-data
    /// dictionary that remembers what the action flashed, and a HTTP context.
    /// </summary>
    public static T Ready<T>(this T controller)
        where T : Controller
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());

        controller.ControllerContext = new ControllerContext(actionContext);
        controller.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        controller.Url = new ConsoleUrlHelper(actionContext);

        return controller;
    }

    /// <summary>The URL helper a unit test can have: the framework's own local-URL rule, and no routing.</summary>
    private sealed class ConsoleUrlHelper : UrlHelperBase
    {
        public ConsoleUrlHelper(ActionContext actionContext)
            : base(actionContext)
        {
        }

        public override string? Action(UrlActionContext actionContext) => "/alerts";

        public override string? Content(string? contentPath) => contentPath;

        public override string? Link(string? routeName, object? values) => null;

        public override string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}

/// <summary>A TempData provider that keeps nothing: the test reads the flash off the same request.</summary>
internal sealed class NullTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

    public void SaveTempData(HttpContext context, IDictionary<string, object> values)
    {
    }
}
