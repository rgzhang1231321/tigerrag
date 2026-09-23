using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>登录或刷新成功后的统一返回结果。权限通路简化为 角色 → 菜单，不再下发权限码集合。</summary>
public sealed record LoginResult(
    UserAccount User,
    AccessToken AccessToken,
    RefreshToken RefreshToken);