namespace TigerRAG.Application.KnowledgeBases;

/// <summary>KB 级 ACL 的 DAL 端口。合并 KB 拥有者、用户 ACL、角色 ACL 三者得到最终可见 KB 范围。</summary>
public interface IKbAccessDal
{
    /// <summary>计算当前用户可访问的 KB Id 集合（Owner ∪ User ACL ∪ Role ACL）。</summary>
    Task<IReadOnlyList<Guid>> GetAccessibleKbIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>整体替换某 KB 的 ACL；非 Admin 调用者必须是 KB 拥有者。</summary>
    Task ReplacePermissionsAsync(
        Guid kbId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>查询 KB 当前 ACL：返回已授权用户 Id 与角色 Id。</summary>
    Task<KbPermissionsSnapshot> GetPermissionsAsync(
        Guid kbId,
        CancellationToken cancellationToken);

    /// <summary>查询 KB 拥有者 Id；KB 不存在时返回 null。</summary>
    Task<Guid?> GetKbOwnerIdAsync(
        Guid kbId,
        CancellationToken cancellationToken);
}
