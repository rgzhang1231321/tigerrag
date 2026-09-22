namespace TigerRAG.Application.Auth;

/// <summary>JWT 访问令牌与其到期时间。仅在内存中传递给调用方。</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);