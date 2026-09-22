namespace TigerRAG.Application.Roles;

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