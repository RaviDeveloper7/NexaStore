// MappingExtensions.cs — wires all Mapster profiles into the global TypeAdapterConfig.
// INTERVIEW: Mapster has a global singleton TypeAdapterConfig.
// All IRegister implementations must be scanned and registered at startup —
// ONCE, not per-request. Calling this in ApplicationServiceRegistration
// ensures it runs exactly once when the application starts.
//
// This is different from AutoMapper where you pass profiles to AddAutoMapper().
// Mapster uses a static config — faster because mapping code is compiled to IL
// at registration time, not resolved via reflection at each map call.

using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace NexaStore.Application.Common.Mappings;

public static class MappingExtensions
{
    public static IServiceCollection AddMapsterMappings(
        this IServiceCollection services)
    {
        // Get the global Mapster configuration singleton
        var config = TypeAdapterConfig.GlobalSettings;

        // INTERVIEW: Scan the Application assembly for all classes implementing IRegister.
        // This automatically picks up ProductMappingProfile, OrderMappingProfile,
        // and any future profiles without needing to register them manually.
        // Same pattern as MediatR and FluentValidation — convention over configuration.
        config.Scan(Assembly.GetExecutingAssembly());

        // Validate all mappings at startup — throws if any destination property
        // is unmapped and IgnoreNonMapped is false.
        // INTERVIEW: Compile() + Validate() at startup catches mapping mismatches
        // at boot time, not at runtime when a user hits the endpoint.
        // "Fail fast" principle — better to crash on startup than silently return
        // null fields to clients in production.
        config.Compile();

        // Register TypeAdapterConfig as singleton — same instance everywhere
        services.AddSingleton(config);

        // Register IMapper as scoped — the mapper wraps the config
        // INTERVIEW: IMapper is Mapster's DI-friendly mapper interface.
        // Handlers inject IMapper and call mapper.Map<TDestination>(source)
        // exactly like AutoMapper — familiar API, better performance.
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
