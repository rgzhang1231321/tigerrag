using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.Infrastructure.Dal;

public sealed class DocumentAccessDal(TigerRagDbContext dbContext) : IDocumentAccessDal
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
        var ownerId = await dbContext.Documents
            .Where(document => document.Id == documentId)
            .Join(
                dbContext.KnowledgeBases,
                document => document.KnowledgeBaseId,
                knowledgeBase => knowledgeBase.Id,
                (_, knowledgeBase) => (Guid?)knowledgeBase.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownerId is null)
        {
            throw new KeyNotFoundException($"Document {documentId} was not found.");
        }

        if (!isAdmin && ownerId != actorId)
        {
            throw new UnauthorizedAccessException("Only the knowledge base owner can change document permissions.");
        }

        var distinctUserIds = userIds.Distinct().ToArray();
        var existingUserCount = await dbContext.Users
            .CountAsync(user => distinctUserIds.Contains(user.Id), cancellationToken);
        if (existingUserCount != distinctUserIds.Length)
        {
            throw new ArgumentException("One or more permission users do not exist.", nameof(userIds));
        }

        var roleIds = await dbContext.Roles
            .Where(role => role.Name != null && roles.Contains(role.Name))
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);
        if (roleIds.Length != roles.Count)
        {
            throw new InvalidOperationException("One or more system roles are missing from the database.");
        }

        var currentPermissions = await dbContext.DocumentPermissions
            .Where(permission => permission.DocumentId == documentId)
            .ToArrayAsync(cancellationToken);
        ApplyPermissionChanges(dbContext, documentId, currentPermissions, distinctUserIds, roleIds);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static void ApplyPermissionChanges(
        TigerRagDbContext dbContext,
        Guid documentId,
        IReadOnlyCollection<DocumentPermissionRecord> currentPermissions,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<Guid> roleIds)
    {
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

    private static DocumentPermissionRecord Permission(
        Guid documentId,
        PermissionPrincipalType principalType,
        Guid principalId) => new()
        {
            DocumentId = documentId,
            PrincipalType = principalType,
            PrincipalId = principalId
        };
}
