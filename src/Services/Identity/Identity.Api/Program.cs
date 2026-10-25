using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Api.Middleware;
using InventorySystem.Identity.Application;
using InventorySystem.Identity.Infrastructure;
using InventorySystem.Identity.Infrastructure.Persistence;
using InventorySystem.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services
    .AddIdentityApplication()
    .AddIdentityInfrastructure();

// This service issues the tokens, and it is also a service that accepts them: /me and the account
// directory are answered from a token, never from a session or a shared secret in a header.
builder.Services.AddInventoryJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Identity Service API",
        Version = "v1",
        Description = "Accounts, roles and the tokens the rest of the system validates (spec section 7.1)."
    });
});

var app = builder.Build();

// Create/upgrade IdentityDb on startup, then make sure a development environment has somebody to
// sign in as. Both are no-ops on a database that is already up to date.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var databaseOptions = scope.ServiceProvider.GetRequiredService<IdentityDatabaseOptions>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityDbInitializer");

    await IdentityDbInitializer.InitializeAsync(dbContext, databaseOptions, logger);

    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();

    await seeder.SeedAsync();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Unexpected error",
        Detail = app.Environment.IsDevelopment() ? feature?.Error.Message : "An unexpected error occurred.",
        Instance = context.Request.Path
    };

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(problem);
}));

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (IdentityDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

    return canConnect
        ? Results.Ok(new { status = "healthy", service = "identity", database = "reachable" })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "IdentityDb is not reachable");
});

app.Run();

/// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program
{
}
