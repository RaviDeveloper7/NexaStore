// ApplicationServiceRegistration.cs — wires the entire Application layer into DI.
// INTERVIEW: Each layer has its own registration extension method.
// Program.cs just calls builder.Services.AddApplicationServices() — it never
// knows the details of what's inside. Clean, self-contained, testable.

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
        // INTERVIEW: Reflection-based registration — no need to register
        // every handler manually. AddMediatR scans the assembly and registers
        // all IRequestHandler<,> and INotificationHandler<> implementations.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        // Register all FluentValidation validators in this assembly.
        // INTERVIEW: Same scan approach — all AbstractValidator<T> classes
        // are registered automatically. ValidationBehaviour picks them up via DI.
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());


        // Mapster — scan assembly for all IRegister profiles, compile and validate
        // INTERVIEW: Must be called after MediatR/FluentValidation registrations
        // because Compile() validates all registered mappings eagerly.
        // Any mapping error surfaces here at startup with a clear exception.
        services.AddMapsterMappings();

        // Register MediatR pipeline behaviours — ORDER MATTERS.
        // INTERVIEW: Behaviours wrap the handler like middleware wraps HTTP requests.
        // Request flows: LoggingBehaviour → ValidationBehaviour → UnhandledExceptionBehaviour → Handler
        // 1. Logging fires first — captures the request before anything can fail
        // 2. Validation fires second — rejects invalid input before hitting the handler
        // 3. UnhandledException fires last — catches anything the handler throws

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
