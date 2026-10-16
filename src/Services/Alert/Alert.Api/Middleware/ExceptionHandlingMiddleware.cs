using InventorySystem.Alerts.Application.Exceptions;
using InventorySystem.Alerts.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DomainException = InventorySystem.Alerts.Domain.Exceptions.DomainException;
using ValidationException = InventorySystem.Alerts.Application.Exceptions.ValidationException;

namespace InventorySystem.Alerts.Api.Middleware;

/// <summary>
/// Maps alert rules and persistence failures onto RFC 7807 ProblemDetails:
///
/// * alert rule violation                  → 400 (DomainException)
/// * request that cannot be satisfied      → 400 (ValidationException)
/// * unknown alert                         → 404 (NotFoundException)
/// * same event applied twice concurrently → 409 (unique key on ProcessedEvents)
/// </summary>
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
            NotFoundException notFound => (StatusCodes.Status404NotFound, "Not found", notFound.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "The alert was changed by another operation. Reload it and try again."),
            DbUpdateException db when IsUniqueConstraintViolation(db) => (StatusCodes.Status409Conflict, "Conflict",
                "This event was already applied. Retry the request."),
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
               || message.Contains("PK_", StringComparison.OrdinalIgnoreCase);
    }
}
