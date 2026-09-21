namespace TigerRAG.Application.Users;

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

/// <summary>JWT 撤权校验器需要的最小用户视图：stamp 与当前是否锁定。</summary>
public sealed record RevocationSnapshot(string SecurityStamp, bool IsLocked);

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
