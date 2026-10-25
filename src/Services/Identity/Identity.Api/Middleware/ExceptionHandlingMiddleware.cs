using InventorySystem.Identity.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DomainException = InventorySystem.Identity.Domain.Exceptions.DomainException;

namespace InventorySystem.Identity.Api.Middleware;


/// Maps account failures onto RFC 7807 ProblemDetails:
///
/// * a rule about accounts broken         → 400 (DomainException)
/// * a credential that was not believed   → 401 (InvalidCredentialsException)
/// * an account or id that is not there   → 404 (NotFoundException)
/// * the same username created twice      → 409 (unique index on Users.Username)


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
            InvalidCredentialsException invalid => (StatusCodes.Status401Unauthorized, "Not authenticated", invalid.Message),
            DomainException domain => (StatusCodes.Status400BadRequest, "Account rule violation", domain.Message),
            NotFoundException notFound => (StatusCodes.Status404NotFound, "Not found", notFound.Message),
            DbUpdateException db when IsUniqueConstraintViolation(db) => (StatusCodes.Status409Conflict, "Conflict",
                "That username already has an account. Choose another sign-in name."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else if (statusCode == StatusCodes.Status401Unauthorized)
        {
            // Never the credential itself, and never whether the username exists: the response body
            // is the whole record of a refused sign-in.
            _logger.LogInformation("Refused a sign-in for {Method} {Path}.", context.Request.Method, context.Request.Path);
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

        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            // Says how to authenticate, without saying what was wrong with the attempt.
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

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
