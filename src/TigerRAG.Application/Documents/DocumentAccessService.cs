using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Roles;

namespace TigerRAG.Application.Documents;

/// <summary>文档级访问范围计算服务。持有 Admin 角色的用户走全放；其他角色合并 KB 拥有者、用户 ACL、角色 ACL。</summary>
public sealed class DocumentAccessService(
    IDocumentAccessDal documentAccess,
    IRoleRegistry roleRegistry)
{
    /// <summary>计算当前用户可见的文档 ID 集合；Admin 短路返回 <c>AllDocuments=true</c>。</summary>
    public async Task<DocumentAccessScope> GetScopeAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (await roleRegistry.UserHasRoleAsync(userId, "Admin", cancellationToken))
        {
            return new DocumentAccessScope(true, []);
        }

        var documentIds = await documentAccess.GetAccessibleDocumentIdsAsync(
            userId,
            roles,
            cancellationToken);
        return new DocumentAccessScope(false, documentIds);
    }

    /// <summary>查询文档当前 ACL；返回用户 Id 与角色名集合。授权规则同 ReplacePermissions。</summary>
    public async Task<DocumentPermissionsDto> GetPermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var ownerId = await documentAccess.GetDocumentOwnerIdAsync(documentId, cancellationToken);
        if (ownerId is null)
        {
            throw new KeyNotFoundException($"Document {documentId} was not found.");
        }
        if (!isAdmin && ownerId != actorId)
        {
            throw new UnauthorizedAccessException("Only the knowledge base owner can view document permissions.");
        }

        var snapshot = await documentAccess.GetPermissionsAsync(documentId, cancellationToken);
        var roleNames = await roleRegistry.GetRoleNamesAsync(snapshot.RoleIds, cancellationToken);

        return new DocumentPermissionsDto(snapshot.UserIds, roleNames);
    }

    /// <summary>替换文档 ACL；仅 Admin 或 KB 拥有者可调用，由 DAL 内部校验。</summary>
    public Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        return documentAccess.ReplacePermissionsAsync(
            documentId,
            actorId,
            isAdmin,
            userIds.Distinct().ToArray(),
            roles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            cancellationToken);
    }
}
