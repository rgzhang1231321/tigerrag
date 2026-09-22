using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 基于 Redis Set 的角色 endpoint 缓存实现。
/// 键：<c>auth:role:{roleName}:endpoints</c>（Redis Set）。
/// TTL：<see cref="GrantCacheOptions.TtlSeconds"/>，默认 300 秒。
/// 不在实现内翻译 Redis 异常——由 <see cref="CachedRoleEndpointGrantStore"/> 决定回退策略。
/// </summary>
public sealed class RedisRoleEndpointGrantCache(
    IConnectionMultiplexer multiplexer,
    IOptions<GrantCacheOptions> options) : IRoleEndpointGrantCache
{
    private static string Key(string roleName) => $"auth:role:{roleName}:endpoints";

    private TimeSpan Ttl => TimeSpan.FromSeconds(options.Value.TtlSeconds);

    public async Task<IReadOnlySet<string>?> GetRoleEndpointsAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        var members = await db.SetMembersAsync(Key(roleName));
        if (members.Length == 0) return null;
        return members.Select(v => v.ToString()).ToHashSet(StringComparer.Ordinal);
    }

    public async Task SetRoleEndpointsAsync(
        string roleName,
        IReadOnlyCollection<string> endpointKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        var key = Key(roleName);

        // 空集合 = 该角色无任何授权：直接删 key；下次读视为 miss，DB 自然返回空集。
        if (endpointKeys.Count == 0)
        {
            await db.KeyDeleteAsync(key);
            return;
        }

        var values = endpointKeys.Select(v => (RedisValue)v).ToArray();

        // 顺序：删旧 → 写新 → 设 TTL。即便中途失败，"key 不存在"语义等价于 miss，
        // 不会出现"空集合被误读为已缓存无授权"的脏命中。
        await db.KeyDeleteAsync(key);
        await db.SetAddAsync(key, values);
        await db.KeyExpireAsync(key, Ttl);
    }

    public async Task InvalidateRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        await db.KeyDeleteAsync(Key(roleName));
    }
}
