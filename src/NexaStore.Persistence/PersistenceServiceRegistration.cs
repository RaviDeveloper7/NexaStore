// Registers all repositories and UnitOfWork.
// INTERVIEW: Every registration here is Scoped — one instance per HTTP request.
// This is correct because:
// - DbContext is Scoped (EF Core requirement — never Singleton)
// - Repositories wrap DbContext — must match its lifetime (Scoped)
// - UnitOfWork wraps DbContext — must be Scoped for the same reason
// Singleton repositories would share one DbContext across all requests,
// causing thread-safety issues and stale cached data — a classic bug.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Persistence.DatabaseContext;
using NexaStore.Persistence.Repositories;

namespace NexaStore.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- DbContext ---
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    // INTERVIEW: EnableRetryOnFailure handles Azure SQL transient faults.
                    // Azure SQL can throttle connections under load — without retry,
                    // these surface as exceptions to the user. With retry, they're silent.
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);

                    sqlOptions.MigrationsAssembly(
                        typeof(AppDbContext).Assembly.FullName);
                });

#if DEBUG
            // INTERVIEW: Only enable sensitive data logging in DEBUG builds.
            // In production, this would log parameter values containing
            // customer emails, passwords, payment details — a GDPR violation.
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
#endif
        });

        // --- Unit of Work ---
        // INTERVIEW: Scoped — one UoW per request, wrapping one DbContext per request.
        // All repository operations in a single request share this UoW instance,
        // so they all participate in the same change-tracking session.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // --- Generic Repository ---
        // INTERVIEW: Open generic registration — AddScoped(typeof(IGenericRepository<>))
        // means DI can resolve IGenericRepository<Product>, IGenericRepository<Category>
        // etc. without registering each one explicitly.
        // When a specific repo is also registered (IProductRepository → ProductRepository),
        // DI resolves the specific one when IProductRepository is requested,
        // and the generic one when IGenericRepository<Product> is requested.
        services.AddScoped(
            typeof(IGenericRepository<>),
            typeof(GenericRepository<>));

        // --- Specific Repositories ---
        // INTERVIEW: Specific repos extend GenericRepository but add domain queries.
        // Registering them separately allows handlers to depend on the specific
        // interface (IProductRepository) and get all domain methods via DI.
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        // --- Outbox Repository ---
        // INTERVIEW: IOutboxRepository is in Application.Common.Interfaces.Services
        // (not Persistence) because the Functions project also depends on it.
        // It's registered here because OutboxRepository (the implementation) is
        // in the Persistence layer — that's where it belongs.
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        return services;
    }
}
