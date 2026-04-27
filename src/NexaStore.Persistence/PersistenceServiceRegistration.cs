// PersistenceServiceRegistration.cs — registers the DbContext and its dependencies.
// INTERVIEW: This is the only place in the solution that knows SQL Server is being used.
// If you switch to PostgreSQL, you change the UseSqlServer call here — nothing else.
// Application layer, handlers, and tests are all completely unaware.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register AppDbContext with SQL Server provider
        // INTERVIEW: The connection string key "DefaultConnection" is the ASP.NET
        // convention — matches what appsettings.json and Azure App Service
        // connection string configuration use by default.
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    // INTERVIEW: EnableRetryOnFailure is production-critical for Azure SQL.
                    // Transient connection failures (network blips, throttling) are retried
                    // automatically. Without this, a momentary Azure SQL hiccup throws an
                    // exception and fails the request.
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);

                    // MigrationsAssembly tells EF where to find migration files
                    // INTERVIEW: Required when DbContext is in a different project
                    // from where you run dotnet-ef migrations — which is our case.
                    sqlOptions.MigrationsAssembly(
                        typeof(AppDbContext).Assembly.FullName);
                });

            // INTERVIEW: In development, log all generated SQL to the console.
            // This is how you catch N+1 queries and missing indexes early.
            // Never leave EnableSensitiveDataLogging on in production — it logs
            // parameter values which may contain PII or secrets.
#if DEBUG
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
#endif
        });

        // Repositories and UoW will be registered here in Week 2 Day 2-3
        // Placeholder comment so you know where they go:
        // services.AddScoped<IUnitOfWork, UnitOfWork>();
        // services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        // services.AddScoped<IProductRepository, ProductRepository>();
        // services.AddScoped<IOrderRepository, OrderRepository>();
        // services.AddScoped<IOutboxRepository, OutboxRepository>();

        return services;
    }
}
