namespace TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

using TigerRAG.Infrastructure.Persistence.Entities.Documents;

/// <summary>KB 级 ACL 记录。复用 PermissionPrincipalType 枚举（User / Role）。</summary>
public sealed class knowledge_base_permission_record
{
    /// <summary>被授权知识库 Id。</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>授权主体类型（用户或角色）。</summary>
    public PermissionPrincipalType PrincipalType { get; set; }

    /// <summary>授权主体 Id；当 PrincipalType=Role 时存角色 Id。</summary>
    public Guid PrincipalId { get; set; }
}
