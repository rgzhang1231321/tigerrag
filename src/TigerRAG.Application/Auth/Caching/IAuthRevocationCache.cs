namespace TigerRAG.Application.Auth;

/// <summary>
/// 按用户 JWT 撤权的本地缓存抽象。键 = user id，值 = 用户当前的 SecurityStamp。
/// 写入缓存的新 stamp 必须与落库值一致；TTL 与 AccessToken 寿命对齐，
/// 保证在飞 token 在缓存存活期内能拿到正确 stamp 完成比对。
/// 实现位于 Infrastructure（基于 Redis）。
/// </summary>
public interface IAuthRevocationCache
{
    /// <summary>取缓存的 stamp；命中失败（未写或已过期）返回 null。</summary>
    Task<string?> GetStampAsync(Guid userId, CancellationToken ct);

    /// <summary>写入当前 stamp；TTL 由实现按 JwtOptions 计算。</summary>
    Task SetStampAsync(Guid userId, string stamp, CancellationToken ct);

    /// <summary>清空缓存项；用户删除或更新敏感状态后调用，避免 TTL 窗口期内的脏命中。</summary>
    Task InvalidateAsync(Guid userId, CancellationToken ct);
}