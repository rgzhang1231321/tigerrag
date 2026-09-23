namespace TigerRAG.Application.Auth;

/// <summary>角色-Endpoint 授权记录。</summary>
public sealed record RoleEndpointGrant(
    string RoleName,
    string MenuKey,
    string EndpointKey,
    DateTimeOffset GrantedAt,
    Guid GrantedBy);