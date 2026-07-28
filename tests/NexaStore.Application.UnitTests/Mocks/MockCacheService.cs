// MockCacheService.cs — in-memory fake of ICacheService.
// IN: A real in-memory Dictionary-backed fake — not Moq — because
// GetProductsQueryHandlerTests needs to verify actual cache-aside behaviour:
// first call misses and populates, second call hits and skips the repository.
// Moq would require manually wiring Setup/Returns per call sequence — the
// hand-written fake naturally supports this because it behaves like real Redis.

using System.Text.Json;
using NexaStore.Application.Common.Interfaces.Services;

namespace NexaStore.Application.UnitTests.Mocks;

public class MockCacheService : ICacheService
{
    // IN: Store serialised JSON, exactly like real Redis does.
    // This also catches serialization bugs a naive object-reference cache would miss.
    private readonly Dictionary<string, string> _store = new();

    // IN: Exposed for tests to assert cache interactions directly —
    // e.g. "was RemoveByPrefixAsync called with the right prefix after an update?"
    public List<string> SetCalls { get; } = new();
    public List<string> RemoveCalls { get; } = new();
    public List<string> RemoveByPrefixCalls { get; } = new();
    public int GetCallCount { get; private set; }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        GetCallCount++;

        if (_store.TryGetValue(key, out var json))
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(
        string key, T value, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        SetCalls.Add(key);
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveCalls.Add(key);
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        RemoveByPrefixCalls.Add(prefix);

        var keysToRemove = _store.Keys
            .Where(k => k.StartsWith(prefix))
            .ToList();

        foreach (var key in keysToRemove)
            _store.Remove(key);

        return Task.CompletedTask;
    }
}
