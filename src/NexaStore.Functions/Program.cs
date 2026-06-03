// Program.cs — the Azure Functions v4 isolated worker entry point.
// IN: Isolated worker = the function runs in a separate .NET process from the
// Azure Functions host. This gives full .NET 8 support, full DI control,
// and no dependency conflicts with the host's own dependencies.
//
// IN: Why reuse AddApplicationServices, AddPersistenceServices, AddInfrastructureServices?
// Functions share the same business logic as the API — DRY principle.
// PlaceOrderCommandHandler, repositories, cache, Service Bus publisher —
// all registered identically. The Functions host is just another entry point
// into the same Application layer. No duplicated business logic anywhere.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexaStore.Application;
using NexaStore.Infrastructure;
using NexaStore.Persistence;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // IN: Same three service registrations as the API's Program.cs.
        // Functions reuse the full Application + Persistence + Infrastructure stack.
        // Identity is intentionally excluded — Functions run as background jobs,
        // not as authenticated HTTP endpoints. They have no concept of
        // "the current user" — they act as the system, not as a user.
        services.AddApplicationServices();
        services.AddPersistenceServices(context.Configuration);
        services.AddInfrastructureServices(context.Configuration);

        // IN: Application Insights for Functions is wired via host.json
        // (applicationInsights section) + the Functions worker package.
        // No explicit AddApplicationInsightsTelemetry() needed here —
        // the Functions SDK handles it automatically when the connection
        // string is present in configuration.
    })
    .Build();

await host.RunAsync();
