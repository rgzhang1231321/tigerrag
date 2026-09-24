using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.Roles;

namespace TigerRAG.Application.KnowledgeBases;

/// <summary>KB 级访问范围计算服务。持有 Admin 角色的用户走全放；其他角色合并 KB 拥有者、用户 ACL、角色 ACL。</summary>
public sealed class KnowledgeBaseAccessService(
    IKbAccessDal kbAccess,
    IRoleRegistry roleRegistry)
{
    /// <summary>计算当前用户可访问的 KB 范围；Admin 短路返回 AllKnowledgeBase=true。</summary>
    public async Task<KbAccessScope> GetScopeAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (await roleRegistry.UserHasRoleAsync(userId, "Admin", cancellationToken))
        {
            return new KbAccessScope(true, []);
        }

        var kbIds = await kbAccess.GetAccessibleKbIdsAsync(userId, roles, cancellationToken);
        return new KbAccessScope(false, kbIds);
    }

    /// <summary>查询 KB 当前 ACL；返回用户 Id 与角色名集合。授权规则同 ReplacePermissions。</summary>
    public async Task<KbPermissionsDto> GetPermissionsAsync(
        Guid kbId,
        Guid actorId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var ownerId = await kbAccess.GetKbOwnerIdAsync(kbId, cancellationToken);
        if (ownerId is null)
        {
            throw new KeyNotFoundException($"Knowledge base {kbId} was not found.");
        }
        if (!isAdmin && ownerId != actorId)
        {
            throw new UnauthorizedAccessException("Only the knowledge base owner can view knowledge base permissions.");
        }

        var snapshot = await kbAccess.GetPermissionsAsync(kbId, cancellationToken);
        var roleNames = await roleRegistry.GetRoleNamesAsync(snapshot.RoleIds, cancellationToken);

        return new KbPermissionsDto(snapshot.UserIds, roleNames);
    }

    /// <summary>替换 KB ACL；仅 Admin 或 KB 拥有者可调用，由 DAL 内部校验。</summary>
    public Task ReplacePermissionsAsync(
        Guid kbId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        return kbAccess.ReplacePermissionsAsync(
            kbId,
            actorId,
            isAdmin,
            userIds.Distinct().ToArray(),
            roles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            cancellationToken);
    }
}
