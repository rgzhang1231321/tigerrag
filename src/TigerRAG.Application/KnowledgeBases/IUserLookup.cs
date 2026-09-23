namespace TigerRAG.Application.KnowledgeBases;

/// <summary>用户信息查询端口；按需扩展（例如取头像、显示名等）。</summary>
public interface IUserLookup
{
    /// <summary>按用户 Id 获取 UserName；用户不存在返回 null。</summary>
    Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken);
}
