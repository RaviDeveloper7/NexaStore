// src/NexaStore.Functions/Program.cs — updated
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexaStore.Application;
using NexaStore.Identity;
using NexaStore.Infrastructure;
using NexaStore.Persistence;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationServices();
        services.AddPersistenceServices(context.Configuration);
        services.AddInfrastructureServices(context.Configuration);

        // IN: Identity added for OrderPlacedConsumerFunction only.
        // Needed to resolve UserManager<ApplicationUser> for customer email lookup.
        // Timer functions (OutboxProcessor, OrderExpiry) do not use Identity —
        // they act as the system, not as authenticated users.
        // AddIdentityServices also registers JWT auth middleware which is harmless
        // in a Functions context — it simply never fires for TimerTrigger functions.
        services.AddIdentityServices(context.Configuration);
    })
    .Build();

await host.RunAsync();
