namespace TigerRAG.Application.ApiLogs;

/// <summary>日志查询 DAL 端口：从 api_log 表读取日志条目。</summary>
public interface IApiLogDal
{
    /// <summary>按条件查询日志条目，返回分页结果。</summary>
    Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken);
}