namespace TigerRAG.Application.Documents;

/// <summary>文档级 ACL 的 DAL 端口。合并 KB 拥有者、用户 ACL、角色 ACL 三者得到最终可见文档范围。</summary>
public interface IDocumentAccessDal
{
    Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>整体替换某文档的 ACL；非 Admin 调用者必须是 KB 拥有者。</summary>
    Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}