// InfrastructureServiceRegistration.cs — wires the entire Infrastructure layer.
// COMPLETE version — covers Cache, Email, and Service Bus (stubs for Day 2).
// IN: Infrastructure is the only layer that knows about external services:
// Redis, Azure Service Bus, SMTP/SendGrid.
// Application layer only knows about IEmailService, ICacheService, IMessageBusPublisher.
// This file is the composition root for those implementations.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Infrastructure.Cache;
using StackExchange.Redis;

namespace NexaStore.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // =====================================================================
        // CACHE — Redis
        // =====================================================================

        // Bind CacheSettings via Options Pattern
        services.Configure<CacheSettings>(
            configuration.GetSection(CacheSettings.SectionName));

        // IN: IConnectionMultiplexer registered as SINGLETON — critical.
        // The connection multiplexer manages a pool of connections to Redis.
        // Creating it per-request (Scoped or Transient) would open a new TCP
        // connection to Redis on every HTTP request — catastrophic at scale.
        // One multiplexer for the application lifetime = one connection pool
        // shared across all requests. This is the StackExchange.Redis design intention.
        //
        // IN: Why not AddStackExchangeRedisCache() (the MS extension)?
        // It registers IDistributedCache — a byte array abstraction that doesn't
        // support pattern-based key deletion. Our RemoveByPrefixAsync needs
        // the SCAN command via IServer — only available from IConnectionMultiplexer directly.
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<CacheSettings>>()
                .Value;

            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                throw new InvalidOperationException(
                    "Redis ConnectionString is not configured. " +
                    "Set 'Redis:ConnectionString' in appsettings.json or User Secrets.");

            var configOptions = ConfigurationOptions.Parse(
                settings.ConnectionString);

            // IN: abortConnect = false — if Redis is unavailable on startup,
            // the app still starts. The first cache operation will fail gracefully
            // (caught in RedisCacheService, logged as warning, treated as cache miss).
            // Without this: app crashes on startup if Redis is unreachable — unacceptable
            // for a system where cache is an optimisation, not a hard dependency.
            configOptions.AbortOnConnectFail = false;

            // IN: connectRetry = 3 — retry connection 3 times before giving up.
            // Handles transient network issues during startup (Azure network flaps etc.)
            configOptions.ConnectRetry = 3;

            // IN: ReconnectRetryPolicy handles reconnection after connection loss.
            // LinearRetry retries every 1 second — balances recovery speed with
            // not hammering a Redis instance that may be under load.
            configOptions.ReconnectRetryPolicy =
                new LinearRetry((int)TimeSpan.FromSeconds(1).TotalMilliseconds);

            var logger = serviceProvider
                .GetRequiredService<ILogger<IConnectionMultiplexer>>();

            var multiplexer = ConnectionMultiplexer.Connect(configOptions);

            // Log connection events — visible in Application Insights
            multiplexer.ConnectionFailed += (_, e) =>
                logger.LogError("Redis connection FAILED: {EndPoint} — {FailureType}",
                    e.EndPoint, e.FailureType);

            multiplexer.ConnectionRestored += (_, e) =>
                logger.LogInformation("Redis connection RESTORED: {EndPoint}",
                    e.EndPoint);

            multiplexer.ErrorMessage += (_, e) =>
                logger.LogWarning("Redis error from {EndPoint}: {Message}",
                    e.EndPoint, e.Message);

            return multiplexer;
        });

        // IN: RedisCacheService is Scoped — not Singleton.
        // IDatabase (obtained from IConnectionMultiplexer in the constructor)
        // is lightweight and safe to use from a Scoped service.
        // Making RedisCacheService Singleton would work too but Scoped is
        // consistent with the other services it collaborates with (repositories).
        services.AddScoped<ICacheService, RedisCacheService>();

        // =====================================================================
        // EMAIL + SERVICE BUS — registered in Week 5 Day 2
        // =====================================================================

        // Placeholder comments — implementations added next session:
        // services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        // services.AddScoped<IEmailService, EmailService>();

        // services.Configure<ServiceBusSettings>(configuration.GetSection(ServiceBusSettings.SectionName));
        // services.AddSingleton<IMessageBusPublisher, AzureServiceBusPublisher>();

        return services;
    }
}
