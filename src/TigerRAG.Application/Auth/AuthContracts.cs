using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>JWT 访问令牌与其到期时间。仅在内存中传递给调用方。</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>刷新令牌原值。仅通过 HttpOnly Cookie 下发，数据库只存哈希。</summary>
public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>绑定到单一刷新会话的用户视图。</summary>
public sealed record RefreshSession(UserAccount User, RefreshToken RefreshToken);

/// <summary>登录或刷新成功后的统一返回结果。权限通路简化为 角色 → 菜单，不再下发权限码集合。</summary>
public sealed record LoginResult(
    UserAccount User,
    AccessToken AccessToken,
    RefreshToken RefreshToken);

/// <summary>刷新会话生命周期 DAL 端口；创建/轮换/撤销/全量撤销。</summary>
public interface IRefreshSessionDal
{
    Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>校验并轮换：原会话被撤销，返回带新令牌的新会话。</summary>
    Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken);

    Task RevokeAsync(string value, CancellationToken cancellationToken);

    /// <summary>用于改密/重置密码后强制全设备下线。</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>根据未撤销的 refresh token 查找关联用户；token 不存在或已撤销返回 null。用于退出登录审计。</summary>
    Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken);
}

/// <summary>JWT 签发端口；Application 不感知 JwtSecurityToken 等具体实现。</summary>
public interface IAccessTokenIssuer
{
    /// <summary>按用户角色签发 JWT；role claim 由 UserAccount.Roles 直接写入。</summary>
    Task<AccessToken> IssueAsync(UserAccount user, CancellationToken cancellationToken);
}

/// <summary>
/// 轮换用户的按用户撤权纪元（Identity SecurityStamp）并把新值写进 JWT 撤权缓存。
/// 实现位于 Infrastructure，避免 Application 直接依赖 ASP.NET Core Identity。
/// 失败抛 <see cref="InvalidOperationException"/>，调用方应让其冒泡以中断正在进行的敏感动作。
/// </summary>
public interface IUserSecurityStampRotator
{
    Task RotateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>用户删除时清空缓存项；防止 TTL 内已失效 stamp 仍命中。</summary>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>首次部署管理员引导端口，由 <c>--bootstrap-admin</c> 命令调用，常规启动不触发。</summary>
public interface IAdminBootstrapper
{
    Task BootstrapAsync(string userName, string password, CancellationToken cancellationToken);
}
