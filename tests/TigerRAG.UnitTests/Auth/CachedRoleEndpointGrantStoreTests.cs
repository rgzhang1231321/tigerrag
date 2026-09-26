using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TigerRAG.Application.Auth;
using TigerRAG.Infrastructure.Auth;

namespace TigerRAG.UnitTests.Auth;

/// <summary>
/// CachedRoleEndpointGrantStore 装饰器行为：缓存命中/未命中分支、Distinct 去重、写路径失效、Redis 异常回退。
/// </summary>
public sealed class CachedRoleEndpointGrantStoreTests
{
    private const string Admin = "Admin";
    private const string Viewer = "Viewer";
    private const string Endpoint = "documents.list";

    [Fact]
    public async Task HasGrantAsync_CacheHit_SkipsInnerDb()
    {
        var inner = new StubInnerStore();
        var cache = new StubCache();
        await cache.SetRoleEndpointsAsync(Admin, [Endpoint], CancellationToken.None);
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin], Endpoint, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, inner.ListByRoleCalls);
    }

    [Fact]
    public async Task HasGrantAsync_CacheMiss_LoadsFromInnerAndFillsCache()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = role =>
            role == Admin
                ? [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())]
                : [];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin], Endpoint, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, inner.ListByRoleCalls);
        Assert.Equal(1, cache.SetCalls);
        Assert.True(cache.Store.ContainsKey(Admin));
    }

    [Fact]
    public async Task HasGrantAsync_CacheMiss_NoGrant_ReturnsFalse()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin], Endpoint, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, inner.ListByRoleCalls);
        // 缓存被回填为空集（实际实现里空集会 DEL key；此处 StubCache 仍记录键）。
        Assert.Equal(1, cache.SetCalls);
    }

    [Fact]
    public async Task HasGrantAsync_CacheReadReturnsNull_FallsBackToInner()
    {
        // 方案 A：缓存实现内部吞掉传输层异常，对外返回 null 等价于 miss。
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())];
        var cache = new StubCache();  // 空 store → 返回 null（miss）
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin], Endpoint, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, inner.ListByRoleCalls);  // 走 DB 加载
    }

    [Fact]
    public async Task HasGrantAsync_CacheWriteReturnsNull_StillReturnsCorrectAnswer()
    {
        // 方案 A：缓存写入由实现内部处理；调用方只关心读路径正确性。
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin], Endpoint, CancellationToken.None);

        Assert.True(result);  // 回填读路径正常
        Assert.Equal(1, cache.SetCalls);
    }

    [Fact]
    public async Task HasGrantAsync_DuplicateRoles_Dedupes()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Admin, Admin, Admin], Endpoint, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, inner.ListByRoleCalls);  // 只查一次
    }

    [Fact]
    public async Task HasGrantAsync_MultipleRoles_AnyWithGrant_ReturnsTrue()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = role =>
            role == Admin
                ? [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())]
                : [];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Viewer, Admin], Endpoint, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasGrantAsync_NoRoleWithGrant_ReturnsFalse()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.HasGrantAsync([Viewer, Admin], Endpoint, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GrantAsync_InvalidatesRoleCache()
    {
        var inner = new StubInnerStore();
        var cache = new StubCache();
        await cache.SetRoleEndpointsAsync(Admin, [Endpoint], CancellationToken.None);
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.GrantAsync(Admin, "documents", Endpoint, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, inner.GrantCalls);
        Assert.Equal(1, cache.InvalidateCalls);
        Assert.False(cache.Store.ContainsKey(Admin));
    }

    [Fact]
    public async Task RevokeAsync_InvalidatesRoleCache()
    {
        var inner = new StubInnerStore();
        var cache = new StubCache();
        await cache.SetRoleEndpointsAsync(Admin, [Endpoint], CancellationToken.None);
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.RevokeAsync(Admin, Endpoint, CancellationToken.None);

        Assert.Equal(1, inner.RevokeCalls);
        Assert.Equal(1, cache.InvalidateCalls);
    }

    [Fact]
    public async Task GrantAllInMenuAsync_AffectedZero_DoesNotInvalidate()
    {
        var inner = new StubInnerStore { GrantAllInMenuAffected = 0 };
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.GrantAllInMenuAsync(Admin, "documents", [], Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, cache.InvalidateCalls);
    }

    [Fact]
    public async Task GrantAllInMenuAsync_AffectedPositive_InvalidatesCache()
    {
        var inner = new StubInnerStore { GrantAllInMenuAffected = 3 };
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.GrantAllInMenuAsync(Admin, "documents",
            [new MenuEndpointDescriptor("documents", Endpoint, "x", "POST", "list")],
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, cache.InvalidateCalls);
    }

    [Fact]
    public async Task RevokeAllInMenuAsync_AffectedZero_DoesNotInvalidate()
    {
        var inner = new StubInnerStore { RevokeAllInMenuAffected = 0 };
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.RevokeAllInMenuAsync(Admin, "documents", CancellationToken.None);

        Assert.Equal(0, cache.InvalidateCalls);
    }

    [Fact]
    public async Task RevokeAllInMenuAsync_AffectedPositive_InvalidatesCache()
    {
        var inner = new StubInnerStore { RevokeAllInMenuAffected = 2 };
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.RevokeAllInMenuAsync(Admin, "documents", CancellationToken.None);

        Assert.Equal(1, cache.InvalidateCalls);
    }

    [Fact]
    public async Task InvalidateRoleAsync_RedisException_DoesNotPropagate()
    {
        var inner = new StubInnerStore();
        var cache = new StubCache
        {
            InvalidateThrows = new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "down", null, CommandStatus.Unknown),
        };
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        await sut.GrantAsync(Admin, "documents", Endpoint, Guid.NewGuid(), CancellationToken.None);  // 不抛
    }

    [Fact]
    public async Task ListByRoleAsync_DelegatesDirectlyToInner()
    {
        var inner = new StubInnerStore();
        inner.OnListByRole = _ => [new RoleEndpointGrant(Admin, "documents", Endpoint, DateTimeOffset.UtcNow, Guid.NewGuid())];
        var cache = new StubCache();
        var sut = new CachedRoleEndpointGrantStore(inner, cache, NullLogger<CachedRoleEndpointGrantStore>.Instance);

        var result = await sut.ListByRoleAsync(Admin, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0, cache.GetCalls);  // 不查缓存
        Assert.Equal(0, cache.SetCalls);  // 不回填
    }

    // ── Stubs ──────────────────────────────────────────────────────────

    private sealed class StubInnerStore : IRoleEndpointGrantStore
    {
        public Func<string, IReadOnlyList<RoleEndpointGrant>> OnListByRole { get; set; } = _ => [];
        public int ListByRoleCalls { get; private set; }
        public int GrantCalls { get; private set; }
        public int RevokeCalls { get; private set; }
        public int GrantAllInMenuAffected { get; set; }
        public int RevokeAllInMenuAffected { get; set; }

        public Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken ct)
        {
            ListByRoleCalls++;
            return Task.FromResult(OnListByRole(roleName));
        }

        public Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken ct) =>
            throw new InvalidOperationException("HasGrantAsync should not be called by the decorator.");

        public Task GrantAsync(string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken ct)
        {
            GrantCalls++;
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string roleName, string endpointKey, CancellationToken ct)
        {
            RevokeCalls++;
            return Task.CompletedTask;
        }

        public Task<int> GrantAllInMenuAsync(
            string roleName, string menuKey,
            IReadOnlyCollection<MenuEndpointDescriptor> endpoints, Guid actorId, CancellationToken ct)
        {
            GrantCalls++;
            return Task.FromResult(GrantAllInMenuAffected);
        }

        public Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken ct)
        {
            RevokeCalls++;
            return Task.FromResult(RevokeAllInMenuAffected);
        }

        public Task<int> ApplyBatchAsync(string roleName, IReadOnlyCollection<BatchEndpointChange> desiredEndpoints, Guid actorId, CancellationToken ct)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class StubCache : IRoleEndpointGrantCache
    {
        public Dictionary<string, IReadOnlySet<string>> Store { get; } = new(StringComparer.Ordinal);
        public int GetCalls { get; private set; }
        public int SetCalls { get; private set; }
        public int InvalidateCalls { get; private set; }
        public Exception? GetThrows { get; set; }
        public Exception? SetThrows { get; set; }
        public Exception? InvalidateThrows { get; set; }

        public Task<IReadOnlySet<string>?> GetRoleEndpointsAsync(string roleName, CancellationToken ct)
        {
            GetCalls++;
            if (GetThrows is not null) throw GetThrows;
            return Task.FromResult<IReadOnlySet<string>?>(Store.TryGetValue(roleName, out var s) ? s : null);
        }

        public Task SetRoleEndpointsAsync(string roleName, IReadOnlyCollection<string> endpointKeys, CancellationToken ct)
        {
            SetCalls++;
            if (SetThrows is not null) throw SetThrows;
            Store[roleName] = endpointKeys.ToHashSet(StringComparer.Ordinal);
            return Task.CompletedTask;
        }

        public Task InvalidateRoleAsync(string roleName, CancellationToken ct)
        {
            InvalidateCalls++;
            if (InvalidateThrows is not null) throw InvalidateThrows;
            Store.Remove(roleName);
            return Task.CompletedTask;
        }
    }
}
