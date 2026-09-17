namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>刷新令牌持久化记录。<see cref="TokenHash"/> 为 SHA-256；<see cref="RevokedAt"/> 非空即视为失效。</summary>
public sealed class refresh_token_record
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
