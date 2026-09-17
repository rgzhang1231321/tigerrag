namespace TigerRAG.Application.Security;

/// <summary>用户与角色管理服务。创建用户拆为两步（先建账号拿到 salt，再设密码）。</summary>
public sealed class UserRoleService(
    IUserDal users,
    IUserCredentialDal credentials,
    IRefreshSessionDal refreshSessions,
    IMenuConfigDal menuConfigs)
{
    /// <summary>列出全部用户及其角色（仅 Admin 角色可通过 Controller 到达）。</summary>
    public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
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

    /// <summary>
    /// 为新创建的用户设置初始密码；成功时撤销该用户的全部刷新会话。
    /// 失败不撤销：避免凭据校验未通过时误杀其他设备的合法会话。
    /// 新用户无会话时 RevokeAllAsync 是 no-op；管理员重复调用则每次都强制下线旧会话。
    /// </summary>
    public async Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await credentials.SetInitialPasswordAsync(userId, passwordHash, cancellationToken);
        await refreshSessions.RevokeAllAsync(userId, cancellationToken);
    }

    /// <summary>管理员重置密码；强制撤销该用户全部刷新会话。盐值复用 DB 中既有值。</summary>
    public async Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        await credentials.ResetPasswordAsync(userId, newPasswordHash, cancellationToken);
        await refreshSessions.RevokeAllAsync(userId, cancellationToken);
    }

    /// <summary>删除用户：先撤销全部刷新会话，再删账号。用户不存在时返回 false。</summary>
    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        await refreshSessions.RevokeAllAsync(userId, cancellationToken);
        return await credentials.DeleteAsync(userId, cancellationToken);
    }

    /// <summary>
    /// 设置用户锁口：锁定时强制撤销全部刷新会话，让已登录用户立刻下线。
    /// <c>lockoutEnd</c> 为 null 表示解锁。
    /// </summary>
    public async Task SetLockoutAsync(
        Guid userId,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
    {
        await credentials.SetLockoutAsync(userId, lockoutEnd, cancellationToken);
        if (lockoutEnd is not null && lockoutEnd > DateTimeOffset.UtcNow)
        {
            await refreshSessions.RevokeAllAsync(userId, cancellationToken);
        }
    }

    /// <summary>列出全部菜单配置，按 SortOrder 排序。</summary>
    public Task<IReadOnlyList<MenuConfigItem>> ListMenuAsync(CancellationToken cancellationToken) =>
        menuConfigs.ListAsync(cancellationToken);

    /// <summary>新建菜单配置。</summary>
    public Task<MenuConfigItem> CreateMenuAsync(
        string key,
        string label,
        string? icon,
        string? permission,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken) =>
        menuConfigs.CreateAsync(key, label, icon, permission, parentId, sortOrder, isEnabled, cancellationToken);

    /// <summary>更新菜单配置；记录不存在时抛出 <see cref="KeyNotFoundException"/>。</summary>
    public async Task<MenuConfigItem> UpdateMenuAsync(
        Guid id,
        string? label,
        string? icon,
        string? permission,
        Guid? parentId,
        int? sortOrder,
        bool? isEnabled,
        CancellationToken cancellationToken)
    {
        var updated = await menuConfigs.UpdateAsync(id, label, icon, permission, parentId, sortOrder, isEnabled, cancellationToken)
            ?? throw new KeyNotFoundException($"Menu config {id} not found.");
        return updated;
    }

    /// <summary>删除菜单配置（级联删除子项）；记录不存在时返回 false。</summary>
    public Task<bool> DeleteMenuAsync(Guid id, CancellationToken cancellationToken) =>
        menuConfigs.DeleteAsync(id, cancellationToken);

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
