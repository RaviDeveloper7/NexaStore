using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexaStore.Persistence;

public static class PersistenceServiceRegistration
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register persistence/data access services here.
        // Example: services.AddDbContext<MyDbContext>(options => ...);
        return services;
    }
}
