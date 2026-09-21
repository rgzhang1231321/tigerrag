using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Roles;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Menus;

namespace TigerRAG.Infrastructure.Menus.Dal;

/// <summary>角色在 menu_config_record.Roles 中的引用关系 DAL。直接走 EF Core 加载并写入；查询走内存过滤（与 RoleAdminService 一致）。</summary>
public sealed class MenuReferenceDal(TigerRagDbContext dbContext) : IRoleMenuReference
{
    public async Task<IReadOnlyList<string>> ListMenuNamesAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var menus = await dbContext.MenuConfigs.ToListAsync(cancellationToken);
        return menus
            .Where(menu => menu.Roles.Contains(roleName, StringComparer.Ordinal))
            .Select(menu => menu.Label)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<int> RemoveRoleFromAllMenusAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var menus = await dbContext.MenuConfigs.ToListAsync(cancellationToken);
        var affected = menus
            .Where(menu => menu.Roles.Contains(roleName, StringComparer.Ordinal))
            .ToList();

        foreach (var menu in affected)
        {
            menu.Roles = menu.Roles.Where(r => !string.Equals(r, roleName, StringComparison.Ordinal)).ToArray();
        }

        if (affected.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return affected.Count;
    }

    /// <summary>将所有 menu_config_record.Roles 数组中的 <paramref name="oldName"/> 替换为 <paramref name="newName"/>；返回被修改的菜单行数。</summary>
    public async Task<int> RenameRoleInAllMenusAsync(string oldName, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var menus = await dbContext.MenuConfigs.ToListAsync(cancellationToken);
        var affected = menus
            .Where(menu => menu.Roles.Contains(oldName, StringComparer.Ordinal))
            .ToList();

        foreach (var menu in affected)
        {
            menu.Roles = menu.Roles
                .Select(r => string.Equals(r, oldName, StringComparison.Ordinal) ? newName : r)
                .ToArray();
        }

        if (affected.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return affected.Count;
    }
}
