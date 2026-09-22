namespace TigerRAG.Infrastructure.Persistence.Entities.Documents;

/// <summary>ACL 主体类型：用户或角色。角色 ACL 会随用户角色变化而生效。</summary>
public enum PermissionPrincipalType
{
    User,
    Role
}