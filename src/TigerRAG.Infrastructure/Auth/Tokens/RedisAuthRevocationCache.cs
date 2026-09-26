using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 基于 Redis 的按用户 stamp 缓存实现。
/// 键：<c>auth:user:{guid}:stamp</c>。
/// TTL：<c>AccessTokenMinutes*60 + ClockSkewSeconds</c>，保证任何在飞 token 在缓存存活期内能拿到正确值。
/// stamp 轮换时调用方应写入新值而非删除，使多实例间缓存与 DB 保持一致。
/// 传输层异常（连接断开、超时）在本类内部记录并吞掉：读 → 返回 null（miss），写 → 忽略。
/// </summary>
public sealed class RedisAuthRevocationCache(
    IConnectionMultiplexer multiplexer,
    IOptions<JwtOptions> options,
    ILogger<RedisAuthRevocationCache> logger) : IAuthRevocationCache
{
    private static string Key(Guid userId) => $"auth:user:{userId}:stamp";

    private TimeSpan Ttl => TimeSpan.FromSeconds(
        (long)options.Value.AccessTokenMinutes * 60 + options.Value.ClockSkewSeconds);

    /// <summary>取缓存的 stamp；命中失败（未写、已过期、或传输层异常）返回 null。</summary>
    public async Task<string?> GetStampAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var db = multiplexer.GetDatabase();
            var value = await db.StringGetAsync(Key(userId));
            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存读取失败；按 miss 处理。userId={UserId}", userId);
            return null;
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存读取异常；按 miss 处理。userId={UserId}", userId);
            return null;
        }
    }

    /// <summary>写入当前 stamp；传输层异常记录后吞掉，不向上抛。</summary>
    public async Task SetStampAsync(Guid userId, string stamp, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(stamp))
        {
            throw new ArgumentException("Stamp must be non-empty.", nameof(stamp));
        }
        try
        {
            var db = multiplexer.GetDatabase();
            await db.StringSetAsync(Key(userId), stamp, Ttl);
        }
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存写入失败；本次跳过。userId={UserId}", userId);
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存写入异常；本次跳过。userId={UserId}", userId);
        }
    }

    /// <summary>清空缓存项；传输层异常记录后吞掉。</summary>
    public async Task InvalidateAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var db = multiplexer.GetDatabase();
            await db.KeyDeleteAsync(Key(userId));
        }
        catch (RedisConnectionException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存失效失败；TTL 兜底。userId={UserId}", userId);
        }
        catch (RedisException error)
        {
            logger.LogWarning(error, "撤权 stamp 缓存失效异常；TTL 兜底。userId={UserId}", userId);
        }
    }
}
