// src/NexaStore.Functions/Program.cs — create this, delete Class1.cs
using Microsoft.Extensions.DependencyInjection;
using NexaStore.Application;
using NexaStore.Infrastructure;
using NexaStore.Persistence;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddApplicationServices();
        services.AddPersistenceServices(ctx.Configuration);
        services.AddInfrastructureServices(ctx.Configuration);
        services.AddApplicationInsightsTelemetry();
    })
    .Build();

await host.RunAsync();
