namespace TigerRAG.Application.Security;

/// <summary>
/// 聚合根对外暴露的用户视图，含 Id、用户名、角色集合与 Identity 的 SecurityStamp。
/// SecurityStamp 由 <c>UserManager.UpdateSecurityStampAsync</c> 维护，是 JWT 撤权比对基准：
/// 任何敏感动作（角色、密码、锁口）轮换该值；签发器把它写入 JWT，校验器把 token 中的
/// claim 与缓存/DB 实时值比对，不一致即视为失效。
/// </summary>
public sealed record UserAccount(Guid Id, string UserName, IReadOnlyList<string> Roles)
{
    /// <summary>当前用户的 Identity SecurityStamp；创建时未填写则默认空串，由 <c>UserDal.MapAsync</c> 填实。</summary>
    public string SecurityStamp { get; init; } = string.Empty;
}

/// <summary>
/// 用户列表行视图：在 <see cref="UserAccount"/> 基础上追加锁口状态，供前端展示与开关。
/// <c>SecurityStamp</c> 在管理页列表里通常不展示，但同一份 DAL 映射复用 <see cref="UserAccount"/> 的字端。
/// </summary>
public sealed record UserListItem(Guid Id, string UserName, IReadOnlyList<string> Roles, bool IsLocked)
{
    public string SecurityStamp { get; init; } = string.Empty;
}

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

    /// <summary>
    /// 按 Id 取当前 stamp 与锁口状态，供 JWT 撤权校验器使用；用户不存在返回 null。
    /// 该路径不走 EF ChangeTracker 之外的状态，与管理页 <c>ListAsync</c> 复用同一映射。
    /// </summary>
    Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}

/// <summary>JWT 撤权校验器需要的最小用户视图：stamp 与当前是否锁定。</summary>
public sealed record RevocationSnapshot(string SecurityStamp, bool IsLocked);

/// <summary>JWT 签发端口；Application 不感知 JwtSecurityToken 等具体实现。</summary>
public interface IAccessTokenIssuer
{
    /// <summary>按用户角色签发 JWT；role claim 由 UserAccount.Roles 直接写入。</summary>
    Task<AccessToken> IssueAsync(UserAccount user, CancellationToken cancellationToken);
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

/// <summary>
/// 数据库事务边界：把"改凭据/角色 → 换 stamp → 撤销刷新会话"三步包在同一事务里。
/// 任一阶段失败即整体回滚，避免凭据已落库而 stamp 未轮换、或 refresh 已撤销而账号未删的不一致状态。
/// Application 通过本接口声明事务需求，不依赖 EF Core 具体 API。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>在同一数据库事务中执行 <paramref name="operation"/>；任一异常即整体回滚。</summary>
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
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

/// <summary>菜单配置视图：前端导航渲染与管理页共用。角色列表为空表示所有人可见。</summary>
public sealed record MenuConfigItem(
    Guid Id,
    string Key,
    string Label,
    string? Icon,
    string[] Roles,
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
        IReadOnlyCollection<string> roles,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <summary>更新菜单配置；记录不存在时返回 null。每个字段用 <see cref="FieldUpdate{T}"/> 包装以区分"不修改"与"清空"。</summary>
    Task<MenuConfigItem?> UpdateAsync(
        Guid id,
        FieldUpdate<string> label,
        FieldUpdate<string> icon,
        FieldUpdate<IReadOnlyCollection<string>> roles,
        FieldUpdate<Guid?> parentId,
        FieldUpdate<int> sortOrder,
        FieldUpdate<bool> isEnabled,
        CancellationToken cancellationToken);

    /// <summary>删除菜单配置及其全部后代（业务级联）；记录不存在时返回 0 表示不存在。</summary>
    Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>角色视图；<c>IsSystem</c> 由 Application 层基于 <see cref="SystemRoles.All"/> 计算，DAL 不返回该标记。</summary>
public sealed record RoleDto(string Name, bool IsSystem);

/// <summary>新建角色请求。</summary>
public sealed record CreateRoleRequest(string Name);

/// <summary>角色管理 DAL 端口：AspNetRoles 写操作与受影响用户/映射查询。</summary>
public interface IRoleAdmin
{
    /// <summary>列出全部角色；<c>IsSystem</c> 始终为 false，由 Application 层覆写。</summary>
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);

    /// <summary>大小写不敏感的存在性校验（Identity NormalizedName）。</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>新建角色，返回视图（<c>IsSystem=false</c>）。</summary>
    Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken);

    /// <summary>当前持有该角色的用户数。</summary>
    Task<int> CountAssignmentsAsync(string name, CancellationToken cancellationToken);

    /// <summary>当前持有该角色的全部用户 Id；删除前用于 stamp 轮换与 refresh 撤销。</summary>
    Task<IReadOnlyList<Guid>> ListAssignedUserIdsAsync(string name, CancellationToken cancellationToken);

    /// <summary>删除 AspNetRoles 行；Identity 内部清 AspNetUserRoles。角色不存在返回 false。</summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken);
}
