// ICacheService.cs — generic cache contract backed by Redis.
// INTERVIEW: The handler never knows it's talking to Redis.
// If you need to swap Redis for in-memory cache in tests, just mock this interface.
// The cache is used in GetProductsQueryHandler for paginated product listings.

namespace NexaStore.Application.Common.Interfaces.Services;

public interface ICacheService
{
    // Retrieve a cached value by key.
    // Returns null if key doesn't exist or has expired.
    // INTERVIEW: Generic so handlers don't need to deserialize manually.
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    // Store a value with an optional TTL (time-to-live).
    // If expiry is null, the key persists until evicted by Redis memory policy.
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    // Invalidate a cached value by key.
    // Called by CreateProduct, UpdateProduct, DeleteProduct handlers
    // to bust the product list cache when catalog data changes.
    // INTERVIEW: Cache invalidation on write is the simplest correct strategy.
    // More sophisticated: cache tags or event-driven invalidation.
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    // Remove all keys matching a pattern — e.g. "products:*"
    // Used to bust all paginated product cache variants in one call
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
