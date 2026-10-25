using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace InventorySystem.BuildingBlocks.Security;

public static class InventoryJwtAuthentication
{
    /// <summary>The path prefix whose requests may carry the token in the query string.</summary>
    public const string HubPathPrefix = "/hubs";

    /// <summary>
    /// Accepts the tokens this system issues: the same issuer, audience and key the Identity service
    /// signs with, read from <c>Jwt:*</c>.
    ///
    /// Registering it does not by itself protect anything — a service still has to say which
    /// endpoints are the authenticated ones.
    /// </summary>
    public static IServiceCollection AddInventoryJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Read once, here, only for the one thing that has to fail at startup: a deployment that has
        // not replaced the published development key.
        JwtOptions
            .FromConfiguration(configuration)
            .EnsureUsableFor(environment.IsProduction());

        // Resolved when the first request needs it rather than when it is registered, so every
        // configuration source is honoured — including ones a test or a deployment adds after this
        // line has run.
        services.TryAddSingleton(serviceProvider => JwtOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((options, serviceProvider) =>
                ConfigureBearer(options, serviceProvider.GetRequiredService<JwtOptions>()));

        return services;
    }

    private static void ConfigureBearer(JwtBearerOptions options, JwtOptions jwt)
    {
        // The claim names stay exactly as they were written ("role", "scope", "sub"), so the
        // vocabulary in this file is the whole story.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            NameClaimType = JwtClaimNames.Name,
            RoleClaimType = JwtClaimNames.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            // A WebSocket handshake cannot carry an Authorization header, so a hub client
            // sends its token as ?access_token=... instead. Only the hub paths are read this
            // way: a token in a URL is exactly the sort of thing that ends up in a log, and
            // there is no reason to accept it anywhere else.
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase)
                    && context.Request.Query.TryGetValue("access_token", out var token)
                    && !string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    }
}
