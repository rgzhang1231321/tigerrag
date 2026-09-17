namespace TigerRAG.Infrastructure.Identity;

/// <summary>JWT 配置映射项；密钥长度 ≥ 32 字节（HS256 安全基线）。</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "TigerRAG";
    public string Audience { get; set; } = "TigerRAG";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
}
