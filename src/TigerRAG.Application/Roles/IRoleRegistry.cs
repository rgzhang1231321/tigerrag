namespace TigerRAG.Application.Roles;

/// <summary>运行时角色与用户-角色关系查询的事实源；取代硬编码 SystemRoles 静态类，提供跨模块共享的角色存在性 / 持有关系查询。</summary>
public interface IRoleRegistry
{
    /// <summary>按 NormalizedName 大小写不敏感判断系统中是否存在指定角色。</summary>
    Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>判断用户是否持有指定角色。</summary>
    Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken);

    /// <summary>列出持有指定角色的用户数；用于自我降级保护等"是否还有别人持有"判定。</summary>
    Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken);
}
