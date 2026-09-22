namespace TigerRAG.Application.Auth;

/// <summary>刷新令牌原值。仅通过 HttpOnly Cookie 下发，数据库只存哈希。</summary>
public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);