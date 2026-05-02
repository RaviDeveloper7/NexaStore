// ApplicationServiceRegistration.cs — wires the entire Application layer into DI.

using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NexaStore.Application.Common.Behaviours;
using NexaStore.Application.Common.Mappings;

namespace NexaStore.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        // Register all MediatR handlers in this assembly automatically.
       
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        // Register all FluentValidation validators in this assembly.
       
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());


        // Mapster — scan assembly for all IRegister profiles, compile and validate
     
        services.AddMapsterMappings();

        // Register MediatR pipeline behaviours — ORDER MATTERS.
      

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(LoggingBehaviour<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehaviour<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(UnhandledExceptionBehaviour<,>));

        return services;
    }
}
