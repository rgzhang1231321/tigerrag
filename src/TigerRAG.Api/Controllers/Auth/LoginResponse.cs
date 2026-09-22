using TigerRAG.Api.Controllers.Users;

namespace TigerRAG.Api.Controllers.Auth;

/// <summary>登录/刷新响应体：访问令牌 + 当前用户视图。</summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserResponse User);