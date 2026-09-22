using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>绑定到单一刷新会话的用户视图。</summary>
public sealed record RefreshSession(UserAccount User, RefreshToken RefreshToken);