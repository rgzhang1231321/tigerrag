namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>文档级 ACL 记录。PrincipalType + PrincipalId 共同决定授权主体（用户或角色）。</summary>
public sealed class document_permission_record
{
    public Guid DocumentId { get; set; }
    public PermissionPrincipalType PrincipalType { get; set; }
    public Guid PrincipalId { get; set; }
}

/// <summary>ACL 主体类型：用户或角色。角色 ACL 会随用户角色变化而生效。</summary>
public enum PermissionPrincipalType
{
    User,
    Role
}
