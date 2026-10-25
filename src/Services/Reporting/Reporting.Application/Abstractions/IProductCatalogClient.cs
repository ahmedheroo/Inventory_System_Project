using InventorySystem.Reports.Application.Catalog;

namespace InventorySystem.Reports.Application.Abstractions;


/// Synchronous access to catalog data owned by the Product service (spec section
/// 8: "use HTTP/REST when the caller needs an immediate response"). The read
/// model only knows product and warehouse ids, so names and unit prices — needed
/// by the valuation report in particular — are resolved here when a report is
/// rendered.

public interface IProductCatalogClient
{
    /// Every product and warehouse, including deactivated ones (matching the read
    /// model, which keeps reporting on stock that still exists).

    Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
