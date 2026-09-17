namespace TigerRAG.Application.Security;

/// <summary>聚合根对外暴露的用户视图，含 Id、用户名与角色集合。</summary>
public sealed record UserAccount(Guid Id, string UserName, IReadOnlyList<string> Roles);

/// <summary>用户列表行视图：在 <see cref="UserAccount"/> 基础上追加锁口状态，供前端展示与开关。</summary>
public sealed record UserListItem(Guid Id, string UserName, IReadOnlyList<string> Roles, bool IsLocked);

/// <summary>JWT 访问令牌与其到期时间。仅在内存中传递给调用方。</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>刷新令牌原值。仅通过 HttpOnly Cookie 下发，数据库只存哈希。</summary>
public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>绑定到单一刷新会话的用户视图。</summary>
public sealed record RefreshSession(UserAccount User, RefreshToken RefreshToken);

/// <summary>登录或刷新成功后的统一返回结果。</summary>
public sealed record LoginResult(UserAccount User, AccessToken AccessToken, RefreshToken RefreshToken);

/// <summary>用户可访问的文档范围：<c>AllDocuments</c>=true 时忽略 <c>DocumentIds</c>。</summary>
public sealed record DocumentAccessScope(bool AllDocuments, IReadOnlyList<Guid> DocumentIds);

/// <summary>用户查询与角色管理的 DAL 端口。</summary>
public interface IUserDal
{
    /// <summary>
    /// 校验客户端提交的密码哈希（<c>MD5(password+salt)</c>）；服务端会用 DB 中的 salt 拼接后交 Identity 走 PBKDF2。
    /// 失败返回 null（不区分原因，防止枚举攻击）。
    /// </summary>
    Task<UserAccount?> ValidateCredentialsAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>取用户的密码盐值；用户不存在时返回 null。该 salt 必须来自 DB，绝不信任客户端。</summary>
    Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken);

    Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}

/// <summary>JWT 签发端口；Application 不感知 JwtSecurityToken 等具体实现。</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(UserAccount user);
}

/// <summary>
/// 用户凭据写操作的 DAL 端口。
/// 客户端提交的 <c>passwordHash</c> 是 <c>MD5(password+salt)</c>；
/// 服务端再用 DB 中的 salt 拼接后交给 Identity 走 PBKDF2 存储。
/// </summary>
public interface IUserCredentialDal
{
    /// <summary>
    /// 仅创建用户并赋角色，生成 salt 落库但不设置密码。
    /// 客户端随后用返回的 salt 计算 MD5 后调用 <see cref="SetInitialPasswordAsync"/>。
    /// </summary>
    Task<UserAccount> CreateAsync(
        string userName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>为刚创建的用户设置初始密码（<c>MD5(password+salt)</c>，salt 来自 DB）。</summary>
    Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken);

    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken);

    /// <summary>删除用户及其全部关联数据（凭据、角色绑定、刷新会话）。用户不存在时返回 false。</summary>
    Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// 设置用户锁口到期时间：<c>lockoutEnd</c> 为 null 表示解锁，为未来时间表示锁定。
    /// 被锁用户的 <c>CheckPasswordSignInAsync</c> 会直接失败，无法登录。
    /// </summary>
    Task SetLockoutAsync(Guid userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken);
}

/// <summary>刷新会话生命周期 DAL 端口；创建/轮换/撤销/全量撤销。</summary>
public interface IRefreshSessionDal
{
    Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>校验并轮换：原会话被撤销，返回带新令牌的新会话。</summary>
    Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken);

    Task RevokeAsync(string value, CancellationToken cancellationToken);

    /// <summary>用于改密/重置密码后强制全设备下线。</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>首次部署管理员引导端口，由 <c>--bootstrap-admin</c> 命令调用，常规启动不触发。</summary>
public interface IAdminBootstrapper
{
    Task BootstrapAsync(string userName, string password, CancellationToken cancellationToken);
}

/// <summary>文档级 ACL 的 DAL 端口。合并 KB 拥有者、用户 ACL、角色 ACL 三者得到最终可见文档范围。</summary>
public interface IDocumentAccessDal
{
    Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>整体替换某文档的 ACL；非 Admin 调用者必须是 KB 拥有者。</summary>
    Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}

/// <summary>菜单配置视图：前端导航渲染与管理页共用。</summary>
public sealed record MenuConfigItem(
    Guid Id,
    string Key,
    string Label,
    string? Icon,
    string? Permission,
    Guid? ParentId,
    int SortOrder,
    bool IsEnabled);

/// <summary>菜单配置 DAL 端口。</summary>
public interface IMenuConfigDal
{
    /// <summary>按 SortOrder 列出全部菜单配置。</summary>
    Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>新建菜单配置，返回落库后的完整视图。</summary>
    Task<MenuConfigItem> CreateAsync(
        string key,
        string label,
        string? icon,
        string? permission,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <summary>更新菜单配置；记录不存在时返回 null。</summary>
    Task<MenuConfigItem?> UpdateAsync(
        Guid id,
        string? label,
        string? icon,
        string? permission,
        Guid? parentId,
        int? sortOrder,
        bool? isEnabled,
        CancellationToken cancellationToken);

    /// <summary>删除菜单配置（级联删除子项）；记录不存在时返回 false。</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
