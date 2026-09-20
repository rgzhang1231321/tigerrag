using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.Infrastructure.Dal;

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
}
