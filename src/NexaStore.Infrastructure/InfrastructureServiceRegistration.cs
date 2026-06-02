// InfrastructureServiceRegistration.cs — COMPLETE version.
// Registers Cache (Redis), Email (SendGrid), and Message Bus (Azure Service Bus).
// IN: Infrastructure is the composition root for all external service integrations.
// The Application layer defines the interfaces. This file wires the implementations.
// Swapping any external service = change this file only.

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Infrastructure.Cache;
using NexaStore.Infrastructure.Mail;
using NexaStore.Infrastructure.MessageBus;
using StackExchange.Redis;

namespace NexaStore.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // =====================================================================
        // CACHE — Redis (from Day 1 — unchanged)
        // =====================================================================

        services.Configure<CacheSettings>(
            configuration.GetSection(CacheSettings.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<CacheSettings>>()
                .Value;

            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                throw new InvalidOperationException(
                    "Redis ConnectionString is not configured. " +
                    "Set 'Redis:ConnectionString' in appsettings.json or User Secrets.");

            var configOptions = ConfigurationOptions
                .Parse(settings.ConnectionString);

            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy =
                new LinearRetry((int)TimeSpan.FromSeconds(1).TotalMilliseconds);

            var logger = serviceProvider
                .GetRequiredService<ILogger<IConnectionMultiplexer>>();
            var multiplexer = ConnectionMultiplexer.Connect(configOptions);

            multiplexer.ConnectionFailed += (_, e) =>
                logger.LogError(
                    "Redis connection FAILED: {EndPoint} — {FailureType}",
                    e.EndPoint, e.FailureType);

            multiplexer.ConnectionRestored += (_, e) =>
                logger.LogInformation(
                    "Redis connection RESTORED: {EndPoint}", e.EndPoint);

            multiplexer.ErrorMessage += (_, e) =>
                logger.LogWarning(
                    "Redis error from {EndPoint}: {Message}", e.EndPoint, e.Message);

            return multiplexer;
        });

        services.AddScoped<ICacheService, RedisCacheService>();

        // =====================================================================
        // EMAIL — SendGrid
        // =====================================================================

        // Bind EmailSettings via Options Pattern
        services.Configure<EmailSettings>(
            configuration.GetSection(EmailSettings.SectionName));

        // IN: EmailService is Scoped — one instance per request/function invocation.
        // It creates a new SendGridClient per send operation (cheap — stateless HTTP client).
        // In production, inject IHttpClientFactory and use a named HttpClient
        // for SendGrid to benefit from connection pooling.
        // For a portfolio project, direct SendGridClient instantiation is acceptable.
        services.AddScoped<IEmailService, EmailService>();

        // =====================================================================
        // MESSAGE BUS — Azure Service Bus
        // =====================================================================

        // Bind ServiceBusSettings via Options Pattern
        services.Configure<ServiceBusSettings>(
            configuration.GetSection(ServiceBusSettings.SectionName));

        // IN: ServiceBusClient registered as Singleton — manages the underlying
        // AMQP connection pool. Creating per-request would open/close TCP connections
        // on every publish — prohibitively expensive at scale.
        // Same rationale as IConnectionMultiplexer for Redis.
        //
        // IN: ServiceBusClientOptions.TransportType = AmqpWebSockets.
        // Standard AMQP uses port 5671. Many corporate firewalls block non-standard ports.
        // WebSockets transport uses port 443 (HTTPS) — always open.
        // Use WebSockets for maximum compatibility, especially in Azure App Service.
        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<ServiceBusSettings>>()
                .Value;

            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                // IN: Return a client with an empty connection string.
                // AzureServiceBusPublisher guards against empty connection string
                // and logs a warning without throwing. App starts cleanly in local dev.
                return new ServiceBusClient(
                    string.Empty,
                    new ServiceBusClientOptions
                    {
                        TransportType = ServiceBusTransportType.AmqpWebSockets
                    });
            }

            return new ServiceBusClient(
                settings.ConnectionString,
                new ServiceBusClientOptions
                {
                    TransportType = ServiceBusTransportType.AmqpWebSockets,

                    // IN: RetryOptions — how the SDK retries transient errors automatically.
                    // MaxRetries = 3: try 3 times before giving up and throwing.
                    // Delay = 1s: initial delay between retries (exponential by default).
                    // MaxDelay = 10s: cap the retry delay at 10 seconds.
                    // Mode = Exponential: 1s → 2s → 4s (capped at 10s).
                    // These retries happen INSIDE the SDK before our code sees the exception.
                    // Our catch block in AzureServiceBusPublisher handles what's left.
                    RetryOptions = new ServiceBusRetryOptions
                    {
                        MaxRetries = 3,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(10),
                        Mode = ServiceBusRetryMode.Exponential
                    }
                });
        });

        // IN: AzureServiceBusPublisher registered as Singleton to match ServiceBusClient.
        // It holds a dictionary of ServiceBusSenders — one per topic.
        // Senders are created lazily and cached. Singleton lifetime ensures
        // the sender cache persists for the app lifetime — maximum connection reuse.
        // IAsyncDisposable ensures proper cleanup on shutdown.
        services.AddSingleton<IMessageBusPublisher, AzureServiceBusPublisher>();

        return services;
    }
}
