namespace TigerRAG.Application.Auth;

/// <summary>角色-Endpoint 授权缓存配置。TTL 边界 = 在最坏情况下授权变更后最长容忍此窗口的脏命中。</summary>
public sealed class GrantCacheOptions
{
    /// <summary>配置节名（appsettings.json 中的键）。</summary>
    public const string DefaultSectionName = "GrantCache";

    /// <summary>角色 endpoint 集合缓存 TTL（秒）；默认 300 秒（5 分钟）。</summary>
    public int TtlSeconds { get; set; } = 300;
}
