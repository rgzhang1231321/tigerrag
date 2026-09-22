namespace TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;

/// <summary>角色-Endpoint 授权记录。行存在 = 授权；删除行 = 撤销。</summary>
public sealed class role_endpoint_grant_record
{
    /// <summary>角色名；与 AspNetRoles.Name 对应，无外键。</summary>
    public required string RoleName { get; set; }

    /// <summary>所属菜单 key；冗余存，供角色授权 UI 按菜单分组。</summary>
    public required string MenuKey { get; set; }

    /// <summary>Endpoint 唯一键；与 [MenuEndpoint(endpointKey:...)] 对应。</summary>
    public required string EndpointKey { get; set; }

    /// <summary>授权时间。</summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>授权操作人 Id。</summary>
    public required Guid GrantedBy { get; set; }
}