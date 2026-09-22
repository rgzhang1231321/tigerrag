namespace TigerRAG.Application.Auth;

/// <summary>角色-Endpoint 授权记录。</summary>
public sealed record RoleEndpointGrant(
    string RoleName,
    string MenuKey,
    string EndpointKey,
    DateTimeOffset GrantedAt,
    Guid GrantedBy);

/// <summary>单条目标授权变更（最终态）：true=授予、false=撤销；仅授予项会落库。</summary>
public sealed record BatchEndpointChange(
    string MenuKey,
    string EndpointKey,
    bool Granted);

/// <summary>角色-Endpoint 授权 DAL 端口。Application 层用它完成授权/撤销/查询。</summary>
public interface IRoleEndpointGrantStore
{
    /// <summary>列出该角色的全部授权记录。</summary>
    Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>判断用户持有的角色中，是否有任一被授予了指定 endpoint。</summary>
    Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken cancellationToken);

    /// <summary>授予单个 endpoint；已存在则幂等（不重复插入）。</summary>
    Task GrantAsync(string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken cancellationToken);

    /// <summary>撤销单个 endpoint 授权；不存在则静默忽略。</summary>
    Task RevokeAsync(string roleName, string endpointKey, CancellationToken cancellationToken);

    /// <summary>批量授予某个菜单下的全部 endpoint；返回实际新增的行数。</summary>
    Task<int> GrantAllInMenuAsync(
        string roleName,
        string menuKey,
        IReadOnlyCollection<MenuEndpointDescriptor> endpoints,
        Guid actorId,
        CancellationToken cancellationToken);

    /// <summary>批量撤销某个菜单下的全部 endpoint；返回实际删除的行数。</summary>
    Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken cancellationToken);

    /// <summary>单事务一次性把角色授权重建为 <paramref name="desiredEndpoints"/> 所表达的目标态；返回实际变更行数（新增+删除之和）。</summary>
    Task<int> ApplyBatchAsync(
        string roleName,
        IReadOnlyCollection<BatchEndpointChange> desiredEndpoints,
        Guid actorId,
        CancellationToken cancellationToken);
}