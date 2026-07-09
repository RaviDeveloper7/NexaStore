using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NexaStore.Api.Middleware;
using NexaStore.Application;
using NexaStore.Identity;
using NexaStore.Infrastructure;
using NexaStore.Persistence;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// Wire all services from each layer
builder.Services.AddApplicationServices();
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddIdentityServices(builder.Configuration);

// IN: AppInsights captures all ILogger calls, requests, queries, and dependencies automatically.
builder.Services.AddApplicationInsightsTelemetry();

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// IN: URL segment versioning (/api/v1/resources) is self-documenting.
builder.Services.AddApiVersioning(options =>
{
    options.ReportApiVersions = true;
    // IN: AssumeDefaultVersionWhenUnspecified allows /api/resources to default to v1.
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

// IN: Health checks expose /health for load balancers and uptime monitoring.
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

var app = builder.Build();

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

// IN: Authentication before Authorization — UseAuthentication reads JWT, UseAuthorization checks [Authorize].
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

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
