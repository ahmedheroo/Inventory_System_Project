namespace Inventory.Web.Models;

/// <summary>
/// A service the console could not read from. The page names the service and quotes
/// what it reported, so "the console is broken" and "the reporting service is down"
/// are not confused with each other.
/// </summary>
public sealed record ServiceErrorViewModel(string Service, string Explanation, int ServiceStatus, int PageStatus)
{
    public string StatusLine => $"{PageStatus} · {Service} answered {ServiceStatus}";
}
