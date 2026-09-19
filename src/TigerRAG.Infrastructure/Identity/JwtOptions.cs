namespace TigerRAG.Infrastructure.Identity;

/// <summary>JWT 配置映射项；密钥长度 ≥ 32 字节（HS256 安全基线）。</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "TigerRAG";
    public string Audience { get; set; } = "TigerRAG";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// JWT 校验时钟宽限（秒）。默认 30 秒，比 JwtBearer 默认 300 秒紧得多，
    /// 用对称密钥 + NTP 同步的环境足以吸收时钟漂移。生产环境若时钟源不可信可上调。
    /// 同一个值也用作 SecurityStamp 缓存 TTL 的安全余量。
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
