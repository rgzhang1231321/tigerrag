using TigerRAG.Application.Auth;
using TigerRAG.Application.Menus;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.Application.Roles;

/// <summary>
/// 角色管理用例：List/Create/Delete/Rename。
/// 删除流程把"删 AspNetRoles → 同步清理 menu_config_record.Roles → N 次 stamp 轮换 → N 次 refresh 撤销"包在同一事务内；
/// 事务失败时对已完成 stamp 轮换的用户调用 <see cref="IUserSecurityStampRotator.InvalidateAsync"/> 清 Redis 缓存，
/// 避免"DB 回滚后 Redis 仍是新 stamp"导致已撤销 JWT 短暂通过校验。
/// 所有角色（含 Admin）平等可改、可删；后端不再有"系统保留集"。
/// </summary>
public sealed class RoleAdminService(
    IRoleAdmin roleAdmin,
    IRoleMenuReference menuReference,
    IMenuConfigDal menuConfigs,
    IUnitOfWork unitOfWork,
    IUserSecurityStampRotator stampRotator,
    IRefreshSessionDal refreshSessions,
    IOperationAuditWriter auditWriter)
{
    /// <summary>列出全部角色；同步注入用户引用数、菜单引用数、菜单名称列表。</summary>
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await roleAdmin.ListAsync(cancellationToken);
        var menus = await menuConfigs.ListAsync(cancellationToken);

        var enriched = new List<RoleDto>(items.Count);
        foreach (var item in items.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var userCount = await roleAdmin.CountAssignmentsAsync(item.Name, cancellationToken);
            var menuNames = menus
                .Where(menu => menu.Roles.Contains(item.Name, StringComparer.Ordinal))
                .Select(menu => menu.Label)
                .OrderBy(label => label, StringComparer.Ordinal)
                .ToArray();
            enriched.Add(new RoleDto(
                item.Name,
                userCount,
                menuNames.Length,
                menuNames));
        }
        return enriched;
    }

    /// <summary>新建角色。校验格式、不重名。所有角色平等，无保留集。</summary>
    /// <exception cref="ArgumentException">格式非法 / 已存在。</exception>
    public async Task<RoleDto> CreateRoleAsync(
        ActorContext actor,
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        RoleDomainService.EnsureName(name);

        if (await roleAdmin.NameExistsAsync(name, cancellationToken))
        {
            throw new ArgumentException($"角色 {name} 已存在。", nameof(request.Name));
        }

        // 角色创建 + 审计写入包在同一事务内：任一失败则全部回滚，避免"角色已建但无审计"的不一致。
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await roleAdmin.CreateRoleAsync(name, innerCt);

            await auditWriter.RecordAsync(new OperationAuditEntry(
                actor.Id,
                actor.Name,
                OperationAuditActions.RoleCreate,
                "role",
                name,
                $"{actor.Name} 创建了角色 {name}"), innerCt);
        }, cancellationToken);

        return new RoleDto(name, 0, 0, []);
    }

    /// <summary>
    /// 删除角色：所有角色平等可删；存在性校验；引用数任一 > 0 拒绝；事务内级联删 menu_config_record.Roles 引用 + 删 AspNetRoles + 轮换受影响用户 stamp + 撤销 refresh + 记录 role.delete 审计。
    /// </summary>
    /// <exception cref="ArgumentException">角色名格式非法 / 仍被用户或菜单引用。</exception>
    /// <returns>true=已删除；false=角色不存在。</returns>
    public async Task<bool> DeleteAsync(
        ActorContext actor,
        string name,
        CancellationToken cancellationToken)
    {
        RoleDomainService.EnsureName(name);

        if (!await roleAdmin.NameExistsAsync(name, cancellationToken))
        {
            return false;
        }

        var userCount = await roleAdmin.CountAssignmentsAsync(name, cancellationToken);
        var menuNames = await menuReference.ListMenuNamesAsync(name, cancellationToken);
        if (userCount > 0)
        {
            throw new ArgumentException(
                $"角色 {name} 仍被 {userCount} 个用户持有，无法删除。",
                nameof(name));
        }
        if (menuNames.Count > 0)
        {
            throw new ArgumentException(
                $"角色 {name} 仍被 {menuNames.Count} 个菜单引用：{string.Join("、", menuNames)}，无法删除。",
                nameof(name));
        }

        // 已完成 stamp 轮换的 uid 列表：用于事务失败时清 Redis 缓存。
        var rotated = new List<Guid>();
        try
        {
            await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var affectedUsers = await roleAdmin.ListAssignedUserIdsAsync(name, innerCt);
                await menuReference.RemoveRoleFromAllMenusAsync(name, innerCt);
                await roleAdmin.DeleteAsync(name, innerCt);
                foreach (var userId in affectedUsers)
                {
                    // 受影响用户必须 stamp 轮换 + refresh 撤销：否则旧 JWT 的 role claim 仍能通过，
                    // 与 AspNetUserRoles 被 Identity 级联清的实际状态不一致。
                    await stampRotator.RotateAsync(userId, innerCt);
                    await refreshSessions.RevokeAllAsync(userId, innerCt);
                    rotated.Add(userId);
                }

                // 审计：role.delete。
                await auditWriter.RecordAsync(new OperationAuditEntry(
                    actor.Id,
                    actor.Name,
                    OperationAuditActions.RoleDelete,
                    "role",
                    name,
                    $"{actor.Name} 删除了角色 {name}（影响 {affectedUsers.Count} 个用户）"), innerCt);
            }, cancellationToken);
        }
        catch
        {
            // DB 已回滚，但 RotateAsync 内的 Redis 写入已在事务外完成；按轮换顺序逐个清缓存。
            foreach (var userId in rotated)
            {
                await stampRotator.InvalidateAsync(userId, cancellationToken);
            }
            throw;
        }

        return true;
    }

    /// <summary>
    /// 重命名角色：新旧名一致幂等返回 true；新名已被占用报错。
    /// 受影响用户在事务内 stamp 轮换 + refresh 撤销，确保重命名后的旧 JWT 立刻失效。
    /// 事务内同步改写 menu_config_record.Roles 引用，并记录 role.rename + menu.references.update 两条审计。
    /// </summary>
    /// <exception cref="ArgumentException">角色名格式非法 / 新名已存在。</exception>
    /// <returns>重命名后的角色视图。</returns>
    public async Task<RoleDto> RenameAsync(
        ActorContext actor,
        string oldName,
        RenameRoleRequest request,
        CancellationToken cancellationToken)
    {
        RoleDomainService.EnsureName(oldName);

        var newName = (request.Name ?? string.Empty).Trim();
        RoleDomainService.EnsureName(newName);
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            // 幂等：直接返回当前视图（保留用户数/菜单引用）。
            var items = await ListAsync(cancellationToken);
            return items.Single(item => string.Equals(item.Name, oldName, StringComparison.Ordinal));
        }

        if (!await roleAdmin.NameExistsAsync(oldName, cancellationToken))
        {
            throw new ArgumentException($"角色 {oldName} 不存在。", nameof(oldName));
        }

        if (await roleAdmin.NameExistsAsync(newName, cancellationToken))
        {
            throw new ArgumentException($"角色 {newName} 已存在。", nameof(request.Name));
        }

        var rotated = new List<Guid>();
        try
        {
            await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var affectedUsers = await roleAdmin.ListAssignedUserIdsAsync(oldName, innerCt);
                await roleAdmin.RenameAsync(oldName, newName, innerCt);
                // 同步改写菜单引用：角色改名后 menu_config_record.Roles 中的旧名必须改为新名，否则菜单可见性断裂。
                var affectedMenus = await menuReference.RenameRoleInAllMenusAsync(oldName, newName, innerCt);
                foreach (var userId in affectedUsers)
                {
                    // 重命名后用户持有的角色名变化，旧 JWT 的 role claim 已不再匹配真实角色，必须轮换。
                    await stampRotator.RotateAsync(userId, innerCt);
                    await refreshSessions.RevokeAllAsync(userId, innerCt);
                    rotated.Add(userId);
                }

                // 审计：role.rename。
                await auditWriter.RecordAsync(new OperationAuditEntry(
                    actor.Id,
                    actor.Name,
                    OperationAuditActions.RoleRename,
                    "role",
                    newName,
                    $"{actor.Name} 将角色 {oldName} 重命名为 {newName}（影响 {affectedUsers.Count} 个用户）"), innerCt);

                // 审计：menu.references.update（仅在确实改写了菜单时记录）。
                if (affectedMenus > 0)
                {
                    await auditWriter.RecordAsync(new OperationAuditEntry(
                        actor.Id,
                        actor.Name,
                        OperationAuditActions.MenuReferencesUpdate,
                        "menu",
                        $"{oldName} → {newName}",
                        $"角色重命名同步改写 {affectedMenus} 个菜单的可见性引用"), innerCt);
                }
            }, cancellationToken);
        }
        catch
        {
            foreach (var userId in rotated)
            {
                await stampRotator.InvalidateAsync(userId, cancellationToken);
            }
            throw;
        }

        return new RoleDto(newName, 0, 0, []);
    }
}
