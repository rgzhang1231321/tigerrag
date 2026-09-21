namespace TigerRAG.Application.ApiLogs;

/// <summary>日志查询服务：编排日志列表查询，供 Controller 调用。</summary>
public sealed class ApiLogService(IApiLogDal apiLogs)
{
    /// <summary>查询日志列表，透传过滤条件与分页参数。</summary>
    public Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken) =>
        apiLogs.ListAsync(request, cancellationToken);
}
