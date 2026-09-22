namespace TigerRAG.Infrastructure.Persistence.Entities.Documents;

/// <summary>文档级 ACL 记录。PrincipalType + PrincipalId 共同决定授权主体（用户或角色）。</summary>
public sealed class document_permission_record
{
    /// <summary>被授权文档 Id。</summary>
    public Guid DocumentId { get; set; }

    /// <summary>授权主体类型（用户或角色）。</summary>
    public PermissionPrincipalType PrincipalType { get; set; }

    /// <summary>授权主体 Id；当 PrincipalType=Role 时存角色 Id。</summary>
    public Guid PrincipalId { get; set; }
}