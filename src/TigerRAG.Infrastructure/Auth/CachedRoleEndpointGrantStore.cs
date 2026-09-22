using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// <see cref="IRoleEndpointGrantStore"/> 的 Redis 缓存装饰器。
/// 缓存键粒度：每个 role 一条 Set，存放该角色全部被授予的 EndpointKey。
/// 读路径：按用户角色逐个 <c>GetRoleEndpointsAsync</c>；miss 时回 <see cref="IRoleEndpointGrantStore.ListByRoleAsync"/> 回填。
/// 写路径：所有 mutating 操作先落 DB，再 <see cref="IRoleEndpointGrantCache.InvalidateRoleAsync"/> 清掉该角色缓存。
/// Redis 不可达时按"缓存 miss"等价处理，写失败被吞（TTL 兜底）。
/// </summary>
public sealed class CachedRoleEndpointGrantStore(
    IRoleEndpointGrantStore inner,
    IRoleEndpointGrantCache cache,
    ILogger<CachedRoleEndpointGrantStore> logger) : IRoleEndpointGrantStore
{
    public Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(
        string roleName, CancellationToken cancellationToken)
        => inner.ListByRoleAsync(roleName, cancellationToken);

    public async Task<bool> HasGrantAsync(
        IEnumerable<string> userRoles, string endpointKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 去重：同一角色多次出现时只查一次缓存。
        foreach (var role in userRoles.Distinct(StringComparer.Ordinal))
        {
            IReadOnlySet<string>? cached = null;
            try
            {
                cached = await cache.GetRoleEndpointsAsync(role, cancellationToken);
            }
            catch (RedisConnectionException error)
            {
                logger.LogWarning(error, "授权缓存读取失败；该角色回退 DB。role={Role}", role);
            }

            if (cached is null)
            {
                // miss：加载全量集合并回填；回填失败不阻塞当前请求判定。
                var grants = await inner.ListByRoleAsync(role, cancellationToken);
                var keys = grants.Select(g => g.EndpointKey).ToList();
                try
                {
                    await cache.SetRoleEndpointsAsync(role, keys, cancellationToken);
                }
                catch (RedisConnectionException error)
                {
                    logger.LogWarning(error, "授权缓存回填失败。role={Role}", role);
                }

                if (keys.Any(k => string.Equals(k, endpointKey, StringComparison.Ordinal)))
                    return true;
            }
            else if (cached.Contains(endpointKey))
            {
                return true;
            }
        }

        return false;
    }

    public async Task GrantAsync(
        string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken cancellationToken)
    {
        await inner.GrantAsync(roleName, menuKey, endpointKey, actorId, cancellationToken);
        await InvalidateQuietlyAsync(roleName, cancellationToken);
    }

    public async Task RevokeAsync(string roleName, string endpointKey, CancellationToken cancellationToken)
    {
        await inner.RevokeAsync(roleName, endpointKey, cancellationToken);
        await InvalidateQuietlyAsync(roleName, cancellationToken);
    }

    public async Task<int> GrantAllInMenuAsync(
        string roleName,
        string menuKey,
        IReadOnlyCollection<MenuEndpointDescriptor> endpoints,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var affected = await inner.GrantAllInMenuAsync(roleName, menuKey, endpoints, actorId, cancellationToken);
        if (affected > 0)
            await InvalidateQuietlyAsync(roleName, cancellationToken);
        return affected;
    }

    public async Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken cancellationToken)
    {
        var affected = await inner.RevokeAllInMenuAsync(roleName, menuKey, cancellationToken);
        if (affected > 0)
            await InvalidateQuietlyAsync(roleName, cancellationToken);
        return affected;
    }

    public async Task<int> ApplyBatchAsync(
        string roleName,
        IReadOnlyCollection<BatchEndpointChange> desiredEndpoints,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var affected = await inner.ApplyBatchAsync(roleName, desiredEndpoints, actorId, cancellationToken);
        if (affected > 0)
            await InvalidateQuietlyAsync(roleName, cancellationToken);
        return affected;
    }

    private async Task InvalidateQuietlyAsync(string roleName, CancellationToken cancellationToken)
    {
        try
        {
            await cache.InvalidateRoleAsync(roleName, cancellationToken);
        }
        catch (RedisConnectionException error)
        {
            // 缓存清理失败：TTL 兜底，调用方无需重试；只记录供运维定位。
            logger.LogWarning(error, "授权缓存失效失败。role={Role}", roleName);
        }
    }
}
