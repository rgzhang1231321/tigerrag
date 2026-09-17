namespace TigerRAG.Infrastructure.Identity;

/// <summary>刷新令牌配置；默认 7 天。改密/重置密码会主动撤销全部会话，TTL 仅作兜底。</summary>
public sealed class RefreshTokenOptions
{
    public int LifetimeDays { get; set; } = 7;
}
