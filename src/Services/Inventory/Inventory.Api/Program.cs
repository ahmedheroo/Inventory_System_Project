using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Stock.Api.Hubs;
using InventorySystem.Stock.Api.Middleware;
using InventorySystem.Stock.Api.Services;
using InventorySystem.Stock.Application;
using InventorySystem.Stock.Application.Abstractions;
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

// Live stock: one notification per committed operation, to every connected client.
// The broadcaster is a singleton because it only holds the hub context, and it is
// registered here rather than in the application layer because SignalR is a transport.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IStockBroadcaster, SignalRStockBroadcaster>();

// The live view is the one door in this service a browser could once walk through: it is not a page
// read, it is a connection that keeps sending. It therefore needs a token — see StockHub — and the
// tokens are the ones the Identity service issues, read from the same Jwt:* configuration.
builder.Services.AddInventoryJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(authorization => authorization.AddPolicy(
    JwtPolicies.StockRead,
    policy => policy.RequireClaim(JwtClaimNames.Scope, JwtScopes.StockRead)));

// Which terminal asked for an operation. The console sends its station in X-Origin-Id, and the
// notification carries it back so a screen can tell its own movements from everybody else's.
// This replaces the application layer's "nobody named it" default.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IStockChangeOrigin, StockChangeOrigin>();

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

// The console runs on another origin: it is a page in a browser, not a service-to-service
// call, and its live client opens a connection to the hub here. In Development any origin
// is allowed, because the console's port belongs to whoever started it; everywhere else the
// origins are named in Cors:AllowedOrigins, and an empty list means no browser may call
// this service cross-origin at all.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

const string WebClientCorsPolicy = "web-client";
builder.Services.AddCors(options => options.AddPolicy(WebClientCorsPolicy, policy =>
{
    if (builder.Environment.IsDevelopment())
    {
        policy.AllowAnyOrigin();
    }
    else if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins);
    }
    else
    {
        return;
    }

    policy.AllowAnyHeader().AllowAnyMethod();
}));

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
}

app.UseCors(WebClientCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StockHub>(StockHub.Path);

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
