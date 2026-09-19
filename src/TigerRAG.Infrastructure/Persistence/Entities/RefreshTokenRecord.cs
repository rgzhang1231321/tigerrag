namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>
/// 刷新令牌持久化记录。<see cref="TokenHash"/> 为 SHA-256；<see cref="RevokedAt"/> 非空即视为失效。
/// <see cref="SecurityStamp"/> 在创建时快照用户当前的 Identity SecurityStamp；<see cref="SecurityStamp"/> 与轮换时
/// 用户的实时 stamp 不一致即视为该令牌已被角色/密码/锁口等敏感动作废止，必须拒绝轮换。
/// </summary>
public sealed class refresh_token_record
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>签发时写入的用户 SecurityStamp；空值视为历史遗留记录，严格匹配将被拒绝。</summary>
    public string? SecurityStamp { get; set; }
}
