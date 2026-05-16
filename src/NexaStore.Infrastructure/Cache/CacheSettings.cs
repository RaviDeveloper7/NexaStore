// CacheSettings.cs — strongly-typed Redis configuration.
// IN: Options Pattern over IConfiguration["Redis:ConnectionString"].
// Magic strings fail silently at runtime. A missing config key returns null
// with no warning — your first sign is a NullReferenceException in production.
// IOptions<CacheSettings> fails fast at startup if the section is missing.
// Strongly typed = validated at boot, not at first cache operation.

namespace NexaStore.Infrastructure.Cache;

public class CacheSettings
{
    // appsettings.json section name — matches GetSection() call in registration
    public const string SectionName = "Redis";

    // Redis connection string — format: "hostname:port,password=xxx,ssl=True"
    // Local dev: "localhost:6379"
    // Azure Cache for Redis: "nexastore.redis.cache.windows.net:6380,password=...,ssl=True"
    // IN: Connection string belongs in User Secrets (dev) or Azure Key Vault (prod).
    // Never in appsettings.json committed to source control.
    public string ConnectionString { get; set; } = string.Empty;

    // Default TTL when SetAsync is called without an explicit expiry.
    // IN: 5 minutes is the NexaStore default — product list cache uses this.
    // Callers can override per-operation (product detail uses 10 min).
    // Having a sensible default prevents "cache forever" bugs when
    // a caller forgets to pass an expiry.
    public int DefaultExpiryMinutes { get; set; } = 5;

    // Redis database index — 0 to 15.
    // IN: Redis supports 16 logical databases on one instance.
    // DB 0 is the default. Useful for isolating environments
    // (dev = DB 1, staging = DB 2) on a shared Redis instance.
    // For Azure Cache for Redis, only DB 0 is supported on Basic/Standard tiers.
    public int DatabaseIndex { get; set; } = 0;

    // Instance name prefix — prepended to every key.
    // IN: Prefix namespaces keys so multiple apps can share one Redis instance
    // without key collisions.
    // "nexastore:" means: "products:p=1:s=10:..." → "nexastore:products:p=1:s=10:..."
    // Essential in shared Redis environments. Harmless in dedicated ones.
    public string InstanceName { get; set; } = "nexastore:";
}
