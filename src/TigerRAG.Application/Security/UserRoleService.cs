namespace TigerRAG.Application.Security;

/// <summary>用户与角色管理服务。创建用户拆为两步（先建账号拿到 salt，再设密码）。</summary>
public sealed class UserRoleService(
    IUserDal users,
    IUserCredentialDal credentials,
    IRefreshSessionDal refreshSessions)
{
    /// <summary>列出全部用户及其角色（仅 Admin 角色可通过 Controller 到达）。</summary>
    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
        users.ListAsync(cancellationToken);

    /// <summary>覆盖式分配角色；空集合表示清空角色。</summary>
    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        await users.AssignRolesAsync(userId, NormalizeRoles(roles), cancellationToken);
    }

    /// <summary>创建用户并赋角色；服务端生成 salt 落库，不在此处设置密码。返回的 <c>UserAccount</c> 暂不含密码。</summary>
    public Task<UserAccount> CreateAsync(
        string userName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken) =>
        credentials.CreateAsync(userName, NormalizeRoles(roles), cancellationToken);

    /// <summary>为新创建的用户设置初始密码；失败时回滚账号创建前的状态（无密码账号不可登录）。</summary>
    public Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken) =>
        credentials.SetInitialPasswordAsync(userId, passwordHash, cancellationToken);

    /// <summary>管理员重置密码；强制撤销该用户全部刷新会话。盐值复用 DB 中既有值。</summary>
    public async Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        await credentials.ResetPasswordAsync(userId, newPasswordHash, cancellationToken);
        await refreshSessions.RevokeAllAsync(userId, cancellationToken);
    }

    // 校验 + 去重 + 排序，保证传给 DAL 的角色集合是确定的、只包含系统已知角色。
    private static string[] NormalizeRoles(IReadOnlyCollection<string> roles)
    {
        var invalidRole = roles.FirstOrDefault(role => !SystemRoles.All.Contains(role));
        if (invalidRole is not null)
        {
            throw new ArgumentException($"Unknown system role: {invalidRole}", nameof(roles));
        }

        return roles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }
}
