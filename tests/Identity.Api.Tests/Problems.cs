using System.Text.Json;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>Reads the part of a refusal that was written for a person.</summary>
internal static class Problems
{
    /// <summary>
    /// The problem+json <c>detail</c>. The trace id beside it differs on every request, so a test that
    /// compares whole bodies is comparing noise; what two refusals have to agree on is this.
    /// </summary>
    public static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("detail", out var detail)
                ? detail.GetString() ?? string.Empty
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
