// RedisCacheService.cs — implements ICacheService using StackExchange.Redis.
// IN: ICacheService is defined in Application.Common.Interfaces.Services.
// This class is the ONLY place in the solution that knows Redis exists.
// Every handler uses ICacheService — none of them reference StackExchange.Redis.
// Swap Redis for Memcached: rewrite this file only. Zero handler changes.
//
// IN: Why StackExchange.Redis over Microsoft.Extensions.Caching.StackExchangeRedis?
// IDistributedCache (the MS abstraction) is convenient but hides Redis features:
// - No prefix support
// - No pattern-based key deletion (RemoveByPrefixAsync needs SCAN command)
// - Serialization is byte[] only — no generic type support
// StackExchange.Redis gives direct access to the full Redis API.
// The trade-off: more code here, but full control and no leaky abstraction.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;
using StackExchange.Redis;

namespace NexaStore.Infrastructure.Cache;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _database;
    private readonly IServer _server;
    private readonly CacheSettings _settings;
    private readonly ILogger<RedisCacheService> _logger;

    // IN: JsonSerializerOptions configured once and reused — not created per call.
    // Creating JsonSerializerOptions per-call is a known .NET performance anti-pattern.
    // It prevents the internal reflection cache from being reused across calls.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false  // Compact JSON in Redis — saves memory
    };

    public RedisCacheService(
        IConnectionMultiplexer connection,
        IOptions<CacheSettings> settings,
        ILogger<RedisCacheService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        // IN: IDatabase is the StackExchange.Redis interface for executing commands.
        // GetDatabase() is cheap — it returns a thin wrapper over the connection.
        // IConnectionMultiplexer is registered as Singleton (one connection pool
        // for the app lifetime). IDatabase is obtained per-operation.
        _database = connection.GetDatabase(_settings.DatabaseIndex);

        // IN: IServer is needed for SCAN commands (RemoveByPrefixAsync).
        // GetServer() requires the endpoint — we take the first connected endpoint.
        // In a Redis Cluster, SCAN only runs on one node — acceptable for our use case.
        // For Redis Cluster with cross-node prefix scanning, use a cluster-aware approach.
        var endpoint = connection.GetEndPoints().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No Redis endpoints are configured. Check CacheSettings:ConnectionString.");

        _server = connection.GetServer(endpoint);
    }

    // =========================================================================
    // GET
    // =========================================================================

    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        // Prepend instance name for key namespacing
        var prefixedKey = BuildKey(key);

        try
        {
            var value = await _database.StringGetAsync(prefixedKey);

            // IN: RedisValue.IsNullOrEmpty checks both Redis null (key not found)
            // and empty string. Either means cache miss — return null.
            if (value.IsNullOrEmpty)
                return default; // null for reference types, default for value types

            // Deserialize from JSON back to T
            // IN: JsonSerializer.Deserialize<T> with our shared options.
            // If deserialization fails (e.g. cached type changed after deployment),
            // we catch, log, and return null — treating it as a cache miss.
            // Better to hit the DB once than to crash the request.
            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (RedisException ex)
        {
            // IN: Redis failure must NEVER break the application.
            // Cache is a performance optimisation — not a system dependency.
            // If Redis is down: log a warning, return null (cache miss),
            // the handler falls through to the DB query.
            // The user sees a slightly slower response. They do NOT see a 500 error.
            // This is the "graceful degradation" pattern for cache failures.
            _logger.LogWarning(ex,
                "Redis GET failed for key '{Key}'. Treating as cache miss.",
                prefixedKey);
            return default;
        }
        catch (JsonException ex)
        {
            // Stale/incompatible cached data — treat as miss, let DB refresh it
            _logger.LogWarning(ex,
                "Redis deserialization failed for key '{Key}'. " +
                "Cached data may be stale or from a previous schema version.",
                prefixedKey);
            return default;
        }
    }

    // =========================================================================
    // SET
    // =========================================================================

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var prefixedKey = BuildKey(key);
        var effectiveExpiry = expiry ?? TimeSpan.FromMinutes(_settings.DefaultExpiryMinutes);

        try
        {
            // Serialize to compact JSON
            var serialized = JsonSerializer.Serialize(value, JsonOptions);

            // IN: StringSetAsync with TimeSpan expiry sets the Redis TTL atomically.
            // The key is created AND the expiry is set in one Redis command (SET key value EX seconds).
            // Using separate SET + EXPIRE commands would create a race condition:
            // if the process crashes between them, the key exists forever (no TTL).
            await _database.StringSetAsync(
                prefixedKey,
                serialized,
                effectiveExpiry);

            _logger.LogDebug(
                "Cache SET: key='{Key}', expiry={Expiry}",
                prefixedKey,
                effectiveExpiry);
        }
        catch (RedisException ex)
        {
            // IN: Same graceful degradation as GET.
            // A failed SET means the next GET will be a cache miss.
            // The system continues correctly — just slower until cache warms up again.
            // Never throw from cache operations — callers don't expect it.
            _logger.LogWarning(ex,
                "Redis SET failed for key '{Key}'. Cache will be cold for this entry.",
                prefixedKey);
        }
    }

    // =========================================================================
    // REMOVE (single key)
    // =========================================================================

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var prefixedKey = BuildKey(key);

        try
        {
            // KeyDeleteAsync returns bool — true if key existed and was deleted.
            // We don't care about the return value — deleting a non-existent key is fine.
            await _database.KeyDeleteAsync(prefixedKey);

            _logger.LogDebug("Cache REMOVE: key='{Key}'", prefixedKey);
        }
        catch (RedisException ex)
        {
            // IN: Failed cache removal is non-fatal.
            // Worst case: stale data is served until TTL expires.
            // The write-through TTL (5-10 min) is the safety net.
            _logger.LogWarning(ex,
                "Redis REMOVE failed for key '{Key}'. " +
                "Stale data may be served until TTL expires.",
                prefixedKey);
        }
    }

    // =========================================================================
    // REMOVE BY PREFIX (bust all paginated cache variants)
    // =========================================================================

    public async Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        // Build the full prefix with instance name
        var fullPrefix = BuildKey(prefix);

        try
        {
            // IN: SCAN is the correct Redis command for key enumeration.
            // NEVER use KEYS * in production — it blocks the Redis event loop
            // for the entire scan duration, making Redis unresponsive to all
            // other commands. KEYS * on a 1M key Redis = seconds of downtime.
            // SCAN uses cursor-based iteration — non-blocking, returns batches.
            // The pattern "prefix*" matches all keys starting with the prefix.
            //
            // IN: _server.KeysAsync returns IAsyncEnumerable<RedisKey>.
            // We collect all matching keys first, then batch-delete.
            // Batch delete (KeyDeleteAsync with array) is one Redis round-trip.
            // Deleting one-by-one in a loop is N round-trips.
            var keys = new List<RedisKey>();

            await foreach (var key in _server.KeysAsync(
                database: _settings.DatabaseIndex,
                pattern: $"{fullPrefix}*"))
            {
                keys.Add(key);
            }

            if (keys.Count == 0)
            {
                _logger.LogDebug(
                    "Cache REMOVE BY PREFIX: no keys found matching '{Prefix}*'",
                    fullPrefix);
                return;
            }

            // Batch delete — one round-trip regardless of how many keys
            await _database.KeyDeleteAsync(keys.ToArray());

            _logger.LogDebug(
                "Cache REMOVE BY PREFIX: deleted {Count} keys matching '{Prefix}*'",
                keys.Count,
                fullPrefix);
        }
        catch (RedisException ex)
        {
            // IN: Graceful degradation — failed prefix removal is non-fatal.
            // Stale paginated results may be served until individual TTLs expire.
            _logger.LogWarning(ex,
                "Redis REMOVE BY PREFIX failed for pattern '{Prefix}*'. " +
                "Stale list cache may be served until TTL expires.",
                fullPrefix);
        }
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    // Builds the fully-qualified Redis key with instance name prefix.
    // IN: All key operations go through BuildKey — the instance prefix is
    // applied consistently. No caller can accidentally bypass namespacing.
    private string BuildKey(string key) => $"{_settings.InstanceName}{key}";
}
