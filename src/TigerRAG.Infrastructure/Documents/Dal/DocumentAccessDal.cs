using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;

namespace TigerRAG.Infrastructure.Documents.Dal;

/// <summary>文档级 ACL DAL。权限范围 = KB 拥有者所属文档 ∪ 用户 ACL ∪ 角色 ACL（取并集）。</summary>
public sealed class DocumentAccessDal(
    TigerRagDbContext dbContext,
    IUnitOfWork unitOfWork) : IDocumentAccessDal
{
    public async Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var roleNames = roles.ToArray();
        var roleIds = dbContext.Roles
            .Where(role => role.Name != null && roleNames.Contains(role.Name))
            .Select(role => role.Id);

        var ownedDocuments = dbContext.Documents
            .Where(document => dbContext.KnowledgeBases
                .Any(knowledgeBase => knowledgeBase.Id == document.KnowledgeBaseId && knowledgeBase.OwnerId == userId))
            .Select(document => document.Id);

        var permittedDocuments = dbContext.DocumentPermissions
            .Where(permission =>
                permission.PrincipalType == PermissionPrincipalType.User && permission.PrincipalId == userId ||
                permission.PrincipalType == PermissionPrincipalType.Role && roleIds.Contains(permission.PrincipalId))
            .Select(permission => permission.DocumentId);

        // 去重交给 Postgres Union；调用方再合并成 DocumentAccessScope。
        return await ownedDocuments.Union(permittedDocuments).ToListAsync(cancellationToken);
    }

    public async Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async ct =>
        {
            // 父行行锁：把同一文档的并发覆盖式 ACL 写入串行化，防止并集残留与删除意图丢失。
            // 默认 READ COMMITTED 下 SELECT FOR UPDATE 会阻塞其他写者，直到本事务提交/回滚。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $""" SELECT 1 FROM "document_record" WHERE "Id" = {documentId} FOR UPDATE """,
                ct);

            var ownerId = await dbContext.Documents
                .Where(document => document.Id == documentId)
                .Join(
                    dbContext.KnowledgeBases,
                    document => document.KnowledgeBaseId,
                    knowledgeBase => knowledgeBase.Id,
                    (_, knowledgeBase) => (Guid?)knowledgeBase.OwnerId)
                .SingleOrDefaultAsync(ct);
            if (ownerId is null)
            {
                throw new KeyNotFoundException($"Document {documentId} was not found.");
            }

            if (!isAdmin && ownerId != actorId)
            {
                // 资源级授权：仅 KB 拥有者或 Admin 可修改 ACL。
                throw new UnauthorizedAccessException("Only the knowledge base owner can change document permissions.");
            }

            var distinctUserIds = userIds.Distinct().ToArray();
            var existingUserCount = await dbContext.Users
                .CountAsync(user => distinctUserIds.Contains(user.Id), ct);
            if (existingUserCount != distinctUserIds.Length)
            {
                throw new ArgumentException("One or more permission users do not exist.", nameof(userIds));
            }

            var roleIds = await dbContext.Roles
                .Where(role => role.Name != null && roles.Contains(role.Name))
                .Select(role => role.Id)
                .ToArrayAsync(ct);
            if (roleIds.Length != roles.Count)
            {
                throw new InvalidOperationException("One or more system roles are missing from the database.");
            }

            var currentPermissions = await dbContext.DocumentPermissions
                .Where(permission => permission.DocumentId == documentId)
                .ToArrayAsync(ct);
            ApplyPermissionChanges(dbContext, documentId, currentPermissions, distinctUserIds, roleIds);
            await dbContext.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    internal static void ApplyPermissionChanges(
        TigerRagDbContext dbContext,
        Guid documentId,
        IReadOnlyCollection<document_permission_record> currentPermissions,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<Guid> roleIds)
    {
        // 整体替换：删除不再需要的主体，追加新增主体；幂等。
        var desired = userIds
            .Select(userId => (PermissionPrincipalType.User, userId))
            .Concat(roleIds.Select(roleId => (PermissionPrincipalType.Role, roleId)))
            .ToHashSet();
        var current = currentPermissions
            .Select(permission => (permission.PrincipalType, permission.PrincipalId))
            .ToHashSet();

        dbContext.DocumentPermissions.RemoveRange(currentPermissions.Where(permission =>
            !desired.Contains((permission.PrincipalType, permission.PrincipalId))));
        dbContext.DocumentPermissions.AddRange(desired
            .Where(principal => !current.Contains(principal))
            .Select(principal => Permission(documentId, principal.Item1, principal.Item2)));
    }

    private static document_permission_record Permission(
        Guid documentId,
        PermissionPrincipalType principalType,
        Guid principalId) => new()
        {
            DocumentId = documentId,
            PrincipalType = principalType,
            PrincipalId = principalId
        };
}
