using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

namespace TigerRAG.Infrastructure.KnowledgeBases.Dal;

/// <summary>KB 级 ACL DAL。权限范围 = KB 拥有者所属 KB ∪ 用户 ACL ∪ 角色 ACL（取并集）。</summary>
public sealed class KbAccessDal(
    TigerRagDbContext dbContext,
    IUnitOfWork unitOfWork) : IKbAccessDal
{
    public async Task<IReadOnlyList<Guid>> GetAccessibleKbIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var roleNames = roles.ToArray();
        var roleIds = dbContext.Roles
            .Where(role => role.Name != null && roleNames.Contains(role.Name))
            .Select(role => role.Id);

        var ownedKbs = dbContext.KnowledgeBases
            .Where(kb => kb.OwnerId == userId)
            .Select(kb => kb.Id);

        var aclKbs = dbContext.KnowledgeBasePermissions
            .Where(permission =>
                (permission.PrincipalType == PermissionPrincipalType.User && permission.PrincipalId == userId) ||
                (permission.PrincipalType == PermissionPrincipalType.Role && roleIds.Contains(permission.PrincipalId)))
            .Select(permission => permission.KnowledgeBaseId);

        // 去重交给 Postgres Union；调用方再合并成 KbAccessScope。
        return await ownedKbs.Union(aclKbs).ToListAsync(cancellationToken);
    }

    public async Task<KbPermissionsSnapshot> GetPermissionsAsync(
        Guid kbId,
        CancellationToken cancellationToken)
    {
        var permissions = await dbContext.KnowledgeBasePermissions
            .Where(permission => permission.KnowledgeBaseId == kbId)
            .ToListAsync(cancellationToken);

        var userIds = permissions
            .Where(permission => permission.PrincipalType == PermissionPrincipalType.User)
            .Select(permission => permission.PrincipalId)
            .ToArray();

        var roleIds = permissions
            .Where(permission => permission.PrincipalType == PermissionPrincipalType.Role)
            .Select(permission => permission.PrincipalId)
            .ToArray();

        return new KbPermissionsSnapshot(userIds, roleIds);
    }

    public async Task<Guid?> GetKbOwnerIdAsync(
        Guid kbId,
        CancellationToken cancellationToken)
    {
        return await dbContext.KnowledgeBases
            .Where(kb => kb.Id == kbId)
            .Select(kb => (Guid?)kb.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ReplacePermissionsAsync(
        Guid kbId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async ct =>
        {
            // 父行行锁：把同一 KB 的并发覆盖式 ACL 写入串行化，防止并集残留与删除意图丢失。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $""" SELECT 1 FROM "knowledge_base_record" WHERE "Id" = {kbId} FOR UPDATE """,
                ct);

            var ownerId = await dbContext.KnowledgeBases
                .Where(kb => kb.Id == kbId)
                .Select(kb => (Guid?)kb.OwnerId)
                .SingleOrDefaultAsync(ct);
            if (ownerId is null)
            {
                throw new KeyNotFoundException($"Knowledge base {kbId} was not found.");
            }

            if (!isAdmin && ownerId != actorId)
            {
                // 资源级授权：仅 KB 拥有者或 Admin 可修改 ACL。
                throw new UnauthorizedAccessException("Only the knowledge base owner can change knowledge base permissions.");
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

            var currentPermissions = await dbContext.KnowledgeBasePermissions
                .Where(permission => permission.KnowledgeBaseId == kbId)
                .ToArrayAsync(ct);
            ApplyKbPermissionChanges(dbContext, kbId, currentPermissions, distinctUserIds, roleIds);
            await dbContext.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>整体替换 KB ACL：删除不再需要的主体，追加新增主体；幂等。</summary>
    internal static void ApplyKbPermissionChanges(
        TigerRagDbContext dbContext,
        Guid kbId,
        IReadOnlyCollection<knowledge_base_permission_record> currentPermissions,
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

        dbContext.KnowledgeBasePermissions.RemoveRange(currentPermissions.Where(permission =>
            !desired.Contains((permission.PrincipalType, permission.PrincipalId))));
        dbContext.KnowledgeBasePermissions.AddRange(desired
            .Where(principal => !current.Contains(principal))
            .Select(principal => KbPermission(kbId, principal.Item1, principal.Item2)));
    }

    /// <summary>构造一条 knowledge_base_permission_record 行；供 ReplacePermissions 批量插入。</summary>
    private static knowledge_base_permission_record KbPermission(
        Guid kbId,
        PermissionPrincipalType principalType,
        Guid principalId) => new()
        {
            KnowledgeBaseId = kbId,
            PrincipalType = principalType,
            PrincipalId = principalId
        };
}
