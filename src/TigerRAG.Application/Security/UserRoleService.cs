namespace TigerRAG.Application.Security;

/// <summary>
/// 用户与角色管理服务。创建用户拆为两步（先建账号拿到 salt，再设密码）。
/// 敏感动作（角色分配、改密、锁定、删除）配套调 <see cref="IUserSecurityStampRotator"/>
/// 与 <see cref="IRefreshSessionDal.RevokeAllAsync"/>，保证旧 JWT 与旧刷新会话同时失效。
/// 角色名校验同时覆盖系统保留名与 IRoleAdmin 中已存在的自定义角色；保留集由 SystemRoles.All 提供。
/// </summary>
public sealed class UserRoleService(
    IUserDal users,
    IUserCredentialDal credentials,
    IRefreshSessionDal refreshSessions,
    IMenuConfigDal menuConfigs,
    IUserSecurityStampRotator stampRotator,
    IUnitOfWork unitOfWork,
    IRoleAdmin roleAdmin)
{
    /// <summary>列出全部用户及其角色（仅 Admin 角色可通过 Controller 到达）。</summary>
    public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
        users.ListAsync(cancellationToken);

    /// <summary>
    /// 覆盖式分配角色；空集合表示清空角色。
    /// 角色变更后必须轮换 stamp 并撤销所有刷新会话——否则降权用户的旧 JWT 仍能继续用旧 role claim 通过授权。
    /// 三步在同一事务内：任一失败整体回滚，避免角色已换而 stamp 未轮换的不一致。
    /// </summary>
    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var normalized = await NormalizeRolesAsync(roles, cancellationToken);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await users.AssignRolesAsync(userId, normalized, ct);
            await stampRotator.RotateAsync(userId, ct);
            await refreshSessions.RevokeAllAsync(userId, ct);
        }, cancellationToken);
    }

    /// <summary>创建用户并赋角色；服务端生成 salt 落库，不在此处设置密码。返回的 <c>UserAccount</c> 暂不含密码。</summary>
    public async Task<UserAccount> CreateAsync(
        string userName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var normalized = await NormalizeRolesAsync(roles, cancellationToken);
        return await credentials.CreateAsync(userName, normalized, cancellationToken);
    }

    /// <summary>
    /// 为新创建的用户设置初始密码。
    /// 成功时轮换 stamp 并撤销该用户的全部刷新会话（旧 stamp 对应的 JWT 立刻失效）。
    /// 三步在同一事务内：stamp 轮换或 refresh 撤销失败则整体回滚，避免密码已改而旧会话仍有效。
    /// </summary>
    public async Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await credentials.SetInitialPasswordAsync(userId, passwordHash, ct);
            await stampRotator.RotateAsync(userId, ct);
            await refreshSessions.RevokeAllAsync(userId, ct);
        }, cancellationToken);
    }

    /// <summary>管理员重置密码；轮换 stamp 并强制撤销该用户全部刷新会话。盐值复用 DB 中既有值。三步同一事务。</summary>
    public async Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await credentials.ResetPasswordAsync(userId, newPasswordHash, ct);
            await stampRotator.RotateAsync(userId, ct);
            await refreshSessions.RevokeAllAsync(userId, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 删除用户：先撤销全部刷新会话与按用户 stamp 缓存，再删账号。
    /// 用户不存在时返回 false。三步同一事务：任一失败整体回滚，避免 refresh 已撤销而账号未删。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var deleted = false;
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await refreshSessions.RevokeAllAsync(userId, ct);
            deleted = await credentials.DeleteAsync(userId, ct);
            // 即便 deleted=false（用户已不存在），仍清缓存，避免脏命中。
            await stampRotator.InvalidateAsync(userId, ct);
        }, cancellationToken);
        return deleted;
    }

    /// <summary>
    /// 设置用户锁口：锁定时轮换 stamp 并撤销全部刷新会话，让已登录用户立刻下线。
    /// 解锁时不动 stamp——已签发的 JWT 仍可继续使用直到自然过期，这是刻意的，
    /// 避免解锁的瞬间把在线用户踢下线。
    /// <c>lockoutEnd</c> 为 null 表示解锁。
    /// 锁定的三步在同一事务内，避免锁口已生效而旧会话仍有效。
    /// </summary>
    public async Task SetLockoutAsync(
        Guid userId,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
    {
        if (lockoutEnd is null || lockoutEnd <= DateTimeOffset.UtcNow)
        {
            // 解锁：仅改锁口字段，无需事务。
            await credentials.SetLockoutAsync(userId, lockoutEnd, cancellationToken);
            return;
        }

        await unitOfWork.ExecuteAsync(async ct =>
        {
            await credentials.SetLockoutAsync(userId, lockoutEnd, ct);
            await stampRotator.RotateAsync(userId, ct);
            await refreshSessions.RevokeAllAsync(userId, ct);
        }, cancellationToken);
    }

    /// <summary>列出全部菜单配置，按 SortOrder 排序。</summary>
    public Task<IReadOnlyList<MenuConfigItem>> ListMenuAsync(CancellationToken cancellationToken) =>
        menuConfigs.ListAsync(cancellationToken);

    /// <summary>新建菜单配置。校验父节点存在性与可见角色名单。</summary>
    /// <exception cref="ArgumentException">业务规则违反。</exception>
    public async Task<MenuConfigItem> CreateMenuAsync(
        string key,
        string label,
        string? icon,
        IReadOnlyCollection<string> roles,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var all = await menuConfigs.ListAsync(cancellationToken);
        if (parentId is not null && !all.Any(item => item.Id == parentId))
            throw new ArgumentException($"父菜单 {parentId} 不存在。", nameof(parentId));
        MenuDomainService.ValidateRoles(roles);

        return await menuConfigs.CreateAsync(key, label, icon, roles, parentId, sortOrder, isEnabled, cancellationToken);
    }

    /// <summary>更新菜单配置；记录不存在时抛出 <see cref="KeyNotFoundException"/>。
    /// 校验父节点合法性（含循环检测）与可见角色名单。</summary>
    /// <exception cref="KeyNotFoundException">菜单不存在。</exception>
    /// <exception cref="ArgumentException">业务规则违反。</exception>
    public async Task<MenuConfigItem> UpdateMenuAsync(
        Guid id,
        FieldUpdate<string> label,
        FieldUpdate<string> icon,
        FieldUpdate<IReadOnlyCollection<string>> roles,
        FieldUpdate<Guid?> parentId,
        FieldUpdate<int> sortOrder,
        FieldUpdate<bool> isEnabled,
        CancellationToken cancellationToken)
    {
        // 仅当显传 roles / parentId 时才校验（Skip 表示不修改，无需校验）
        if (roles.HasValue)
        {
            MenuDomainService.ValidateRoles(roles.Value);
        }
        if (parentId.HasValue)
        {
            var all = await menuConfigs.ListAsync(cancellationToken);
            MenuDomainService.ValidateParent(id, parentId.Value, all);
        }

        var updated = await menuConfigs.UpdateAsync(id, label, icon, roles, parentId, sortOrder, isEnabled, cancellationToken)
            ?? throw new KeyNotFoundException($"Menu config {id} not found.");
        return updated;
    }

    /// <summary>删除菜单配置及其全部后代（业务级联）；记录不存在时返回 false。</summary>
    public async Task<bool> DeleteMenuAsync(Guid id, CancellationToken cancellationToken)
    {
        var count = await menuConfigs.DeleteSubtreeAsync(id, cancellationToken);
        return count > 0;
    }

    // 校验角色存在性（系统保留集 ∪ DB 中已存在的自定义角色）+ 去重 + 排序。
    // 系统保留名无需走 DB：始终合法（SeedRoles 兜底）；自定义角色由 IRoleAdmin 实时校验。
    private async Task<string[]> NormalizeRolesAsync(
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        foreach (var role in roles)
        {
            if (RoleDomainService.IsReserved(role))
            {
                continue;
            }
            if (!await roleAdmin.NameExistsAsync(role, cancellationToken))
            {
                throw new ArgumentException($"未知角色：{role}", nameof(roles));
            }
        }

        return roles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }
}
