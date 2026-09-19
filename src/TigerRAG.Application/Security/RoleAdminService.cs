namespace TigerRAG.Application.Security;

/// <summary>
/// 角色管理用例：List/Create/Delete。
/// 删除流程把"删 AspNetRoles → N 次 stamp 轮换 → N 次 refresh 撤销"包在同一事务内；
/// 事务失败时对已完成 stamp 轮换的用户调用 <see cref="IUserSecurityStampRotator.InvalidateAsync"/> 清 Redis 缓存，
/// 避免"DB 回滚后 Redis 仍是新 stamp"导致已撤销 JWT 短暂通过校验。
/// </summary>
public sealed class RoleAdminService(
    IRoleAdmin roleAdmin,
    IUnitOfWork unitOfWork,
    IUserSecurityStampRotator stampRotator,
    IRefreshSessionDal refreshSessions)
{
    /// <summary>列出全部角色；<c>IsSystem</c> 由 <see cref="RoleDomainService.IsReserved"/> 计算并覆写 DAL 返回值。</summary>
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await roleAdmin.ListAsync(cancellationToken);
        return items
            .Select(item => new RoleDto(item.Name, RoleDomainService.IsReserved(item.Name)))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>新建角色。校验格式、非保留名、不重名。</summary>
    /// <exception cref="ArgumentException">格式非法 / 保留名 / 已存在。</exception>
    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        RoleDomainService.EnsureName(name);
        if (RoleDomainService.IsReserved(name))
        {
            throw new ArgumentException($"系统角色 {name} 已存在，不可重建。", nameof(request.Name));
        }

        if (await roleAdmin.NameExistsAsync(name, cancellationToken))
        {
            throw new ArgumentException($"角色 {name} 已存在。", nameof(request.Name));
        }

        return await roleAdmin.CreateRoleAsync(name, cancellationToken);
    }

    /// <summary>删除角色：保留名 / 存在性两道前置闸；事务内级联删 AspNetRoles + 轮换受影响用户 stamp + 撤销 refresh。</summary>
    /// <exception cref="ArgumentException">保留名 / 角色名格式非法。</exception>
    /// <returns>true=已删除；false=角色不存在。</returns>
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        RoleDomainService.EnsureName(name);
        if (RoleDomainService.IsReserved(name))
        {
            throw new ArgumentException($"系统角色 {name} 不可删除。", nameof(name));
        }

        if (!await roleAdmin.NameExistsAsync(name, cancellationToken))
        {
            return false;
        }

        // 已完成 stamp 轮换的 uid 列表：用于事务失败时清 Redis 缓存。
        var rotated = new List<Guid>();
        try
        {
            await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var affectedUsers = await roleAdmin.ListAssignedUserIdsAsync(name, innerCt);
                await roleAdmin.DeleteAsync(name, innerCt);
                foreach (var userId in affectedUsers)
                {
                    // 受影响用户必须 stamp 轮换 + refresh 撤销：否则旧 JWT 的 role claim 仍能通过，
                    // 与 AspNetUserRoles 被 Identity 级联清的实际状态不一致。
                    await stampRotator.RotateAsync(userId, innerCt);
                    await refreshSessions.RevokeAllAsync(userId, innerCt);
                    rotated.Add(userId);
                }
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
}
