using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Web.Api;

public sealed record DownloadedFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// The shared half of the service clients: JSON round-trips, and the promise that
/// **any** non-success answer becomes an <see cref="ApiException"/> naming the
/// service and quoting its problem detail. Without that, a 503 from a service the
/// console depends on would be indistinguishable from "this list is empty".
/// </summary>
public abstract class ApiClientBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // The services serialize enums as numbers; the console models them as enums.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    protected ApiClientBase(HttpClient httpClient, ILogger logger, string serviceName)
    {
        _httpClient = httpClient;
        _logger = logger;
        ServiceName = serviceName;
    }

    /// <summary>How the console names this service when it fails, e.g. "Product service".</summary>
    protected string ServiceName { get; }

    protected Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Get, path, body: null, cancellationToken);

    /// <summary>Reads a resource, treating 404 as "not there" rather than as a failure.</summary>
    protected async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync<T>(path, cancellationToken);
        }
        catch (ApiException exception) when (exception.IsNotFound)
        {
            return default;
        }
    }

    protected Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    protected Task<T> PutAsync<T>(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Put, path, body, cancellationToken);

    protected async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        await SendCoreAsync<object>(method, path, body, cancellationToken);

    protected async Task<DownloadedFile> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path, body: null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, path, cancellationToken);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? response.RequestMessage?.RequestUri?.Segments.LastOrDefault()
                       ?? "download";

        return new DownloadedFile(
            fileName,
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            content);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        await SendCoreAsync<T>(method, path, body, cancellationToken);

    private async Task<T> SendCoreAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(method, path, body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, path, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default!;
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

        return payload ?? throw new ApiException(
            response.StatusCode,
            ServiceName,
            $"The {ServiceName} answered {method} {path} with an empty body.",
            "An empty payload where the console expected data.");
    }

    private async Task<HttpResponseMessage> ExecuteAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "{Service} could not be reached ({Method} {Path}).", ServiceName, method, path);

            throw new ApiException(
                HttpStatusCode.ServiceUnavailable,
                ServiceName,
                $"The {ServiceName} could not be reached.",
                exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "{Service} timed out ({Method} {Path}).", ServiceName, method, path);

            throw new ApiException(
                HttpStatusCode.GatewayTimeout,
                ServiceName,
                $"The {ServiceName} did not answer in time.",
                $"{method} {path}");
        }
    }

    private async Task<ApiException> FailureAsync(
        HttpResponseMessage response,
        string path,
        CancellationToken cancellationToken)
    {
        var detail = await ReadProblemDetailAsync(response, cancellationToken);

        _logger.LogWarning(
            "{Service} answered {Status} for {Method} {Path}: {Detail}",
            ServiceName,
            (int)response.StatusCode,
            response.RequestMessage?.Method,
            path,
            detail);

        return new ApiException(
            response.StatusCode,
            ServiceName,
            $"The {ServiceName} answered {(int)response.StatusCode} for {path}.",
            detail);
    }

    /// <summary>Pulls the human-readable part out of a problem+json body.</summary>
    private static async Task<string?> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(JsonOptions, cancellationToken);

            if (problem is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(problem.Detail))
            {
                return problem.Detail;
            }

            if (problem.Errors is { Count: > 0 })
            {
                return string.Join(
                    " ",
                    problem.Errors.Select(entry => $"{entry.Key}: {string.Join(" ", entry.Value)}"));
            }

            return problem.Title;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException)
        {
            // Not a problem+json body (a proxy error page, an empty response): the
            // status code is all we have, and the caller already has it.
            return null;
        }
    }

    private sealed record ProblemPayload(
        string? Title,
        string? Detail,
        int? Status,
        Dictionary<string, string[]>? Errors);
}

/// <summary>Builds the query string of a service call, skipping what was not asked for.</summary>
internal static class ApiQuery
{
    /// <summary>
    /// Formats the values the way the services bind them: dates round-trip (ISO 8601),
    /// booleans are lower case, enums are their names (the services accept either).
    /// </summary>
    public static string Build(params (string Name, object? Value)[] parameters)
    {
        var parts = parameters
            .Where(parameter => !IsEmpty(parameter.Value))
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(Format(parameter.Value!))}")
            .ToList();

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static bool IsEmpty(object? value) =>
        value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),
            _ => false
        };

    private static string Format(object value) => value switch
    {
        DateTime timestamp => timestamp.ToString("O"),
        bool flag => flag ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
