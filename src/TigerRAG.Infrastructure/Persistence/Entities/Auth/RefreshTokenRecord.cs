namespace TigerRAG.Infrastructure.Persistence.Entities.Auth;

/// <summary>
/// 刷新令牌持久化记录。<see cref="TokenHash"/> 为 SHA-256；<see cref="RevokedAt"/> 非空即视为失效。
/// <see cref="SecurityStamp"/> 在创建时快照用户当前的 Identity SecurityStamp；<see cref="SecurityStamp"/> 与轮换时
/// 用户的实时 stamp 不一致即视为该令牌已被角色/密码/锁口等敏感动作废止，必须拒绝轮换。
/// </summary>
public sealed class refresh_token_record
{
    /// <summary>刷新令牌主键。</summary>
    public Guid Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public Guid UserId { get; set; }

    /// <summary>令牌原值的 SHA-256 哈希（原值不入库）。</summary>
    public required string TokenHash { get; set; }

    /// <summary>过期时间；超过此时间的令牌在轮换前必须拒绝。</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>撤销时间；非空即视为失效，不可再用于轮换。</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>签发时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>签发时写入的用户 SecurityStamp；空值视为历史遗留记录，严格匹配将被拒绝。</summary>
    public string? SecurityStamp { get; set; }
}
