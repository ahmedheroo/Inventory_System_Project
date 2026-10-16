using InventorySystem.Stock.Api.Middleware;
using InventorySystem.Stock.Application;
using InventorySystem.Stock.Infrastructure;
using InventorySystem.Stock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services
    .AddInventoryApplication()
    .AddInventoryInfrastructure();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Inventory Service API",
        Version = "v1",
        Description = "Owns inventory quantities and stock transactions (spec section 7.2)."
    });
});

// The MVC web app runs on a different origin in development.
const string WebClientCorsPolicy = "web-client";
builder.Services.AddCors(options => options.AddPolicy(
    WebClientCorsPolicy,
    policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Create/upgrade InventoryDb on startup so a fresh clone works without manual steps.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var databaseOptions = scope.ServiceProvider.GetRequiredService<InventoryDatabaseOptions>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryDbInitializer");

    await InventoryDbInitializer.InitializeAsync(dbContext, databaseOptions, logger);
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
    app.UseCors(WebClientCorsPolicy);
}

app.MapControllers();

app.MapGet("/health", async (InventoryDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

    return canConnect
        ? Results.Ok(new { status = "healthy", service = "inventory", database = "reachable" })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "InventoryDb is not reachable");
});

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program
{
}
