namespace TigerRAG.Application.Roles;

/// <summary>角色视图：含用户引用数与关联菜单名称列表；所有角色平等，无系统/自定义区分。</summary>
public sealed record RoleDto(
    string Name,
    int UserCount,
    int MenuCount,
    IReadOnlyList<string> MenuNames);

/// <summary>新建角色请求。</summary>
public sealed record CreateRoleRequest(string Name);

/// <summary>重命名角色请求。</summary>
public sealed record RenameRoleRequest(string Name);

/// <summary>角色管理 DAL 端口：AspNetRoles 写操作与受影响用户/映射查询。</summary>
public interface IRoleAdmin
{
    /// <summary>列出全部角色；引用计数与关联菜单由 Application 层覆盖。</summary>
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);

    /// <summary>大小写不敏感的存在性校验（Identity NormalizedName）。</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>新建角色，返回视图。</summary>
    Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken);

    /// <summary>当前持有该角色的用户数。</summary>
    Task<int> CountAssignmentsAsync(string name, CancellationToken cancellationToken);

    /// <summary>当前持有该角色的全部用户 Id；删除前用于 stamp 轮换与 refresh 撤销。</summary>
    Task<IReadOnlyList<Guid>> ListAssignedUserIdsAsync(string name, CancellationToken cancellationToken);

    /// <summary>删除 AspNetRoles 行；Identity 内部清 AspNetUserRoles。角色不存在返回 false。</summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>仅修改 AspNetRoles.Name，RoleId 保持不变；Identity 内部同步刷新 NormalizedName。</summary>
    /// <returns>true=已改名；false=新旧名相同（幂等）。</returns>
    Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken);
}

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
