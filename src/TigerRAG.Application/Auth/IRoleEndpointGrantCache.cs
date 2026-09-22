namespace TigerRAG.Application.Auth;

/// <summary>角色-Endpoint 授权的按角色全量集缓存；与 HasGrantAsync 解耦，便于测试替换与降级到 DB。</summary>
public interface IRoleEndpointGrantCache
{
    /// <summary>读取某角色的全量授权 endpoint key 集合；返回 null 表示缓存未命中或不可用。</summary>
    Task<IReadOnlySet<string>?> GetRoleEndpointsAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>写入某角色的全量授权 endpoint key 集合；TTL 由实现控制。</summary>
    Task SetRoleEndpointsAsync(string roleName, IReadOnlyCollection<string> endpointKeys, CancellationToken cancellationToken);

    /// <summary>使指定角色的缓存失效；grant/revoke 写入路径调用。</summary>
    Task InvalidateRoleAsync(string roleName, CancellationToken cancellationToken);
}
