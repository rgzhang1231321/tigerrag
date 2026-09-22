namespace TigerRAG.Application.Shared;

/// <summary>
/// 数据库事务边界：把"改凭据/角色 → 换 stamp → 撤销刷新会话"三步包在同一事务里。
/// 任一阶段失败即整体回滚，避免凭据已落库而 stamp 未轮换、或 refresh 已撤销而账号未删的不一致状态。
/// Application 通过本接口声明事务需求，不依赖 EF Core 具体 API。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>在同一数据库事务中执行 <paramref name="operation"/>；任一异常即整体回滚。</summary>
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}