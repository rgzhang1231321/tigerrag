namespace TigerRAG.Application.Roles;

/// <summary>角色在 menu_config_record.Roles 数组中的引用关系端口。Application 层用它做引用计数展示与删除前的级联清理。</summary>
public interface IRoleMenuReference
{
    /// <summary>返回所有 Roles JSONB 数组中包含 <paramref name="roleName"/> 的菜单项（Label 列表，按 Label 排序）。</summary>
    Task<IReadOnlyList<string>> ListMenuNamesAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>从所有 menu_config_record.Roles 数组里移除 <paramref name="roleName"/>；返回被修改的菜单行数。空数组行保留为空数组。</summary>
    Task<int> RemoveRoleFromAllMenusAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>将所有 menu_config_record.Roles 数组中的 <paramref name="oldName"/> 替换为 <paramref name="newName"/>；返回被修改的菜单行数。角色重命名时在事务内调用。</summary>
    Task<int> RenameRoleInAllMenusAsync(string oldName, string newName, CancellationToken cancellationToken);
}