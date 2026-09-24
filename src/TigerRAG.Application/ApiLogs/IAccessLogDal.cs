namespace TigerRAG.Application.ApiLogs;

/// <summary>访问日志查询 DAL 端口：从 api_access_log 表读取访问条目。</summary>
public interface IAccessLogDal
{
    /// <summary>按条件查询访问日志条目，返回分页结果。</summary>
    Task<AccessLogQueryResult> ListAsync(AccessLogQueryRequest request, CancellationToken cancellationToken);
}
