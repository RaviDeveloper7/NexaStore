// Program.cs — the API composition root. Every layer meets here.
// IN: Program.cs has one job: wire all services together and configure the pipeline.
// No business logic lives here. Service registration is delegated to each layer's
// own extension method (AddApplicationServices, AddPersistenceServices etc.)
// This keeps Program.cs readable regardless of how complex the individual layers are.

using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NexaStore.Api.Middleware;
using NexaStore.Application;
using NexaStore.Identity;
using NexaStore.Infrastructure;
using NexaStore.Persistence;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// SERVICE REGISTRATION
// =========================================================================

// Application layer — MediatR, FluentValidation, Mapster, pipeline behaviours
builder.Services.AddApplicationServices();

// Persistence layer — EF Core, repositories, Unit of Work
builder.Services.AddPersistenceServices(builder.Configuration);

// Infrastructure layer — Redis, SendGrid, Azure Service Bus
builder.Services.AddInfrastructureServices(builder.Configuration);

// Identity layer — ASP.NET Core Identity, JWT authentication, AuthService
builder.Services.AddIdentityServices(builder.Configuration);

// Application Insights — structured telemetry, request tracking, dependency tracking
// IN: One line replaces Serilog + Seq + a custom sink. AppInsights captures all
// ILogger calls, HTTP requests, SQL queries, Redis calls, HTTP client calls.
// Zero extra code in handlers or middleware — all captured automatically.
builder.Services.AddApplicationInsightsTelemetry();

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// =========================================================================
// API VERSIONING
// =========================================================================

// IN: URL segment versioning (/api/v1/products) is the most explicit
// and widely understood approach. Alternatives:
// - Query string (?api-version=1.0) — less visible, easy to forget
// - Header (api-version: 1.0) — clean URLs but hidden from docs
// URL versioning is self-documenting — the version is visible in every request.
builder.Services.AddApiVersioning(options =>
{
    // Return API version info in response headers:
    // api-supported-versions: 1.0
    // api-deprecated-versions: (when applicable)
    options.ReportApiVersions = true;

    // IN: AssumeDefaultVersionWhenUnspecified = true means requests to
    // /api/products (no version) are treated as /api/v1/products.
    // Useful during transition — existing clients without version in the URL
    // still work after versioning is added.
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);

    // Use URL segment format: /api/v{version}/...
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    // Format version in Swagger group names: "v1", "v2"
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// =========================================================================
// SWAGGER / OPENAPI
// =========================================================================

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>,
    ConfigureSwaggerOptions>();

builder.Services.AddSwaggerGen(options =>
{
    // IN: JWT Bearer auth in Swagger UI — lets you test protected endpoints
    // directly from the browser without Postman.
    // The "Authorize" button in Swagger accepts: Bearer {your_jwt_token}
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description =
            "Enter your JWT token. Example: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Include XML comments in Swagger UI (/// summary tags on controller actions)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// =========================================================================
// HEALTH CHECKS
// =========================================================================

// IN: Health checks expose /health endpoint for Azure App Service health probes,
// load balancer health checks, and uptime monitoring.
// Each check verifies connectivity to a critical dependency.
// If SQL Server is unreachable, the health check returns Unhealthy
// and the load balancer can route traffic away from this instance.
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sql-server",
        tags: new[] { "db", "sql" })
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: new[] { "cache", "redis" })
    .AddAzureServiceBusTopic(
        builder.Configuration["ServiceBus:ConnectionString"]!,
        builder.Configuration["ServiceBus:OrderPlacedTopic"]!,
        name: "service-bus",
        tags: new[] { "messaging", "servicebus" });

// =========================================================================
// BUILD
// =========================================================================

var app = builder.Build();

// =========================================================================
// MIDDLEWARE PIPELINE — ORDER MATTERS
// =========================================================================

// IN: ExceptionMiddleware must be FIRST — it wraps the entire pipeline.
// If any middleware below it throws, ExceptionMiddleware catches it and
// returns a clean JSON error response. Without it being first, exceptions
// from authentication middleware or routing would produce HTML error pages.
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Generate Swagger endpoints for all API versions
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                $"NexaStore API {description.GroupName.ToUpperInvariant()}");
        }

        // Swagger UI at root — http://localhost:5000/swagger
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// IN: Authentication before Authorization — always.
// UseAuthentication: reads the JWT, validates it, populates HttpContext.User.
// UseAuthorization: reads HttpContext.User, checks [Authorize] attributes.
// Reversing the order means [Authorize] checks an unauthenticated User — always fails.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// IN: /health returns JSON with individual check statuses.
// Azure App Service uses this URL for health probes — configure in Portal.
app.MapHealthChecks("/health");

app.Run();

// =========================================================================
// SWAGGER OPTIONS HELPER
// =========================================================================

// IN: IConfigureOptions<SwaggerGenOptions> is the correct pattern for
// configuring Swagger with API versioning — generates one Swagger document
// per API version automatically.
public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(
                description.GroupName,
                new OpenApiInfo
                {
                    Title = "NexaStore API",
                    Version = description.ApiVersion.ToString(),
                    Description = description.IsDeprecated
                        ? "NexaStore API — this version is deprecated."
                        : "Enterprise E-Commerce Order Management API. " +
                          "Built with .NET 8 · Clean Architecture · CQRS · Azure.",
                    Contact = new OpenApiContact
                    {
                        Name = "NexaStore",
                        Url = new Uri("https://github.com/RaviDeveloper7/NexaStore")
                    }
                });
        }
    }
}
