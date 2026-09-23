using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 基于 Redis 的按用户 stamp 缓存实现。
/// 键：<c>auth:user:{guid}:stamp</c>。
/// TTL：<c>AccessTokenMinutes*60 + ClockSkewSeconds</c>，保证任何在飞 token 在缓存存活期内能拿到正确值。
/// stamp 轮换时调用方应写入新值而非删除，使多实例间缓存与 DB 保持一致。
/// </summary>
public sealed class RedisAuthRevocationCache(
    IConnectionMultiplexer multiplexer,
    IOptions<JwtOptions> options) : IAuthRevocationCache
{
    private static string Key(Guid userId) => $"auth:user:{userId}:stamp";

    private TimeSpan Ttl => TimeSpan.FromSeconds(
        (long)options.Value.AccessTokenMinutes * 60 + options.Value.ClockSkewSeconds);

    public async Task<string?> GetStampAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        var value = await db.StringGetAsync(Key(userId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task SetStampAsync(Guid userId, string stamp, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(stamp))
        {
            throw new ArgumentException("Stamp must be non-empty.", nameof(stamp));
        }
        var db = multiplexer.GetDatabase();
        await db.StringSetAsync(Key(userId), stamp, Ttl);
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        await db.KeyDeleteAsync(Key(userId));
    }
}