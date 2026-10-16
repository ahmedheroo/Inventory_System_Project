using InventorySystem.Stock.Application.Exceptions;
using InventorySystem.Stock.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = InventorySystem.Stock.Application.Exceptions.ValidationException;

namespace InventorySystem.Stock.Api.Middleware;

/// Maps stock business rules and cross-service failures 
/// ProblemDetails responses:
///
/// * over-issue / negative quantity           → 400 (DomainException)
/// * unknown or deactivated product/warehouse → 400 (ValidationException)
/// * Product service unreachable              → 503 (CatalogUnavailableException)
/// * concurrent modification of the same row  → 409 (DbUpdateConcurrencyException)

public class ExceptionHandlingMiddleware
{
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
            DomainException domain => (StatusCodes.Status400BadRequest, "Business rule violation", domain.Message),
            ValidationException validation => (StatusCodes.Status400BadRequest, "Validation failed", validation.Message),
            CatalogUnavailableException catalog => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable", catalog.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "The stock level was changed by another operation. Reload it and try again."),
            DbUpdateException db when IsUniqueConstraintViolation(db) => (StatusCodes.Status409Conflict, "Conflict",
                "Another operation created the same stock record at the same time. Retry the request."),
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
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("IX_", StringComparison.Ordinal);
    }
}
