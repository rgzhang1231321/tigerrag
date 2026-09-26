using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Auth;
using TigerRAG.Infrastructure.Auth;

namespace TigerRAG.UnitTests.Auth;

/// <summary>RedisRoleEndpointGrantCache 真 Redis 行为：SetMembers/SetAdd/Expire/Delete 端到端。Redis 不可达时整个 fixture 跳过。</summary>
public sealed class RedisRoleEndpointGrantCacheTests : IDisposable
{
    private static readonly string ConnectionString =
        "localhost:6379,abortConnect=false,connectTimeout=100,syncTimeout=100";

    private readonly ConnectionMultiplexer? _multiplexer;
    private readonly RedisRoleEndpointGrantCache? _cache;
    private readonly string _roleName = $"test-role-{Guid.NewGuid():N}";
    private readonly bool _redisAvailable;

    public RedisRoleEndpointGrantCacheTests()
    {
        try
        {
            _multiplexer = ConnectionMultiplexer.Connect(ConnectionString);
            _redisAvailable = _multiplexer.IsConnected;
            if (_redisAvailable)
            {
                var options = Options.Create(new GrantCacheOptions { TtlSeconds = 60 });
                _cache = new RedisRoleEndpointGrantCache(_multiplexer, options, NullLogger<RedisRoleEndpointGrantCache>.Instance);
            }
        }
        catch
        {
            _redisAvailable = false;
        }
    }

    public void Dispose()
    {
        try { _cache?.InvalidateRoleAsync(_roleName, CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        _multiplexer?.Dispose();
    }

    [Fact]
    public async Task SetThenGet_RoundTripsEndpointKeys()
    {
        if (!_redisAvailable) return;  // 跳过：本地 Redis 不可达

        await _cache!.SetRoleEndpointsAsync(_roleName, ["ep.a", "ep.b", "ep.c"], CancellationToken.None);
        var got = await _cache.GetRoleEndpointsAsync(_roleName, CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal(3, got!.Count);
        Assert.Contains("ep.a", got);
        Assert.Contains("ep.b", got);
        Assert.Contains("ep.c", got);
    }

    [Fact]
    public async Task GetRoleEndpointsAsync_MissingKey_ReturnsNull()
    {
        if (!_redisAvailable) return;

        var freshRole = $"absent-{Guid.NewGuid():N}";
        var got = await _cache!.GetRoleEndpointsAsync(freshRole, CancellationToken.None);

        Assert.Null(got);
    }

    [Fact]
    public async Task SetEmptyCollection_RemovesKey()
    {
        if (!_redisAvailable) return;

        var role = $"empty-{Guid.NewGuid():N}";
        await _cache!.SetRoleEndpointsAsync(role, ["x"], CancellationToken.None);
        await _cache.SetRoleEndpointsAsync(role, Array.Empty<string>(), CancellationToken.None);
        var got = await _cache.GetRoleEndpointsAsync(role, CancellationToken.None);

        Assert.Null(got);
    }

    [Fact]
    public async Task InvalidateRoleAsync_RemovesKey()
    {
        if (!_redisAvailable) return;

        await _cache!.SetRoleEndpointsAsync(_roleName, ["ep.a"], CancellationToken.None);
        await _cache.InvalidateRoleAsync(_roleName, CancellationToken.None);
        var got = await _cache.GetRoleEndpointsAsync(_roleName, CancellationToken.None);

        Assert.Null(got);
    }

    [Fact]
    public async Task SetRoleEndpointsAsync_AppliesTtl()
    {
        if (!_redisAvailable) return;

        var ttlCache = new RedisRoleEndpointGrantCache(
            _multiplexer!,
            Options.Create(new GrantCacheOptions { TtlSeconds = 60 }),
            NullLogger<RedisRoleEndpointGrantCache>.Instance);
        var role = $"ttl-{Guid.NewGuid():N}";
        await ttlCache.SetRoleEndpointsAsync(role, ["x"], CancellationToken.None);

        var ttl = await _multiplexer!.GetDatabase().KeyTimeToLiveAsync($"auth:role:{role}:endpoints");

        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 1, 61);

        await ttlCache.InvalidateRoleAsync(role, CancellationToken.None);
    }
}
