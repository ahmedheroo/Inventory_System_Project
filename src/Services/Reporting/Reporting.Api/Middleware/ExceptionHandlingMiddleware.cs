using InventorySystem.Reports.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Reports.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    public const string ProblemContentType = "application/problem+json";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(exception, "Response already started; cannot write an error response.");
                throw;
            }

            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException validation => (StatusCodes.Status400BadRequest, "Validation failed", validation.Message),
            CatalogUnavailableException catalog => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable", catalog.Message),
            ArgumentOutOfRangeException argument => (StatusCodes.Status400BadRequest, "Validation failed",
                $"'{argument.ParamName}' is not valid: {argument.Message}"),
            DbUpdateException db when IsUniqueConstraintViolation(db) => (StatusCodes.Status409Conflict, "Conflict",
                "This event was already projected. Retry the request."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation("{Status} for {Method} {Path}: {Detail}", statusCode, context.Request.Method, context.Request.Path, detail);
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = statusCode;

        // The media type is passed in: WriteAsJsonAsync would otherwise set its own
        // (application/json) and the client would not see the RFC 7807 type.
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: ProblemContentType,
            cancellationToken: context.RequestAborted);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("PK_", StringComparison.OrdinalIgnoreCase);
    }
}
