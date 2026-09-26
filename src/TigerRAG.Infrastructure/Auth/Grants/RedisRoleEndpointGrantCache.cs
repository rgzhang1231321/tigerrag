using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 基于 Redis Set 的角色 endpoint 缓存实现。
/// 键：<c>auth:role:{roleName}:endpoints</c>（Redis Set）。
/// TTL：<see cref="GrantCacheOptions.TtlSeconds"/>，默认 300 秒。
/// 传输层异常（连接断开、超时）在本类内部记录并吞掉：读 → 返回 null（miss），写/失效 → 忽略。
/// 调用方只需处理 null（回退 DB），不再 catch 实现异常。
/// </summary>
public sealed class RedisRoleEndpointGrantCache(
    IConnectionMultiplexer multiplexer,
    IOptions<GrantCacheOptions> options,
    ILogger<RedisRoleEndpointGrantCache> logger) : IRoleEndpointGrantCache
{
    private static string Key(string roleName) => $"auth:role:{roleName}:endpoints";

    private TimeSpan Ttl => TimeSpan.FromSeconds(options.Value.TtlSeconds);

    /// <summary>读取某角色的全量授权 endpoint key 集合；返回 null 表示缓存未命中、TTL 过期、或传输层异常。</summary>
    public async Task<IReadOnlySet<string>?> GetRoleEndpointsAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var db = multiplexer.GetDatabase();
            var members = await db.SetMembersAsync(Key(roleName));
            if (members.Length == 0) return null;
            return members.Select(v => v.ToString()).ToHashSet(StringComparer.Ordinal);
        }
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "授权缓存读取失败；按 miss 处理。role={Role}", roleName);
            return null;
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "授权缓存读取异常；按 miss 处理。role={Role}", roleName);
            return null;
        }
    }

    /// <summary>写入某角色的全量授权 endpoint key 集合；TTL 由实现控制。传输层异常记录后吞掉。</summary>
    public async Task SetRoleEndpointsAsync(
        string roleName,
        IReadOnlyCollection<string> endpointKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
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
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "授权缓存写入失败；本次跳过。role={Role}", roleName);
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "授权缓存写入异常；本次跳过。role={Role}", roleName);
        }
    }

    /// <summary>使指定角色的缓存失效；传输层异常记录后吞掉。</summary>
    public async Task InvalidateRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var db = multiplexer.GetDatabase();
            await db.KeyDeleteAsync(Key(roleName));
        }
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "授权缓存失效失败；TTL 兜底。role={Role}", roleName);
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "授权缓存失效异常；TTL 兜底。role={Role}", roleName);
        }
    }
}
