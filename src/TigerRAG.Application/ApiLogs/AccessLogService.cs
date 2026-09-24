namespace TigerRAG.Application.ApiLogs;

/// <summary>访问日志查询服务：编排访问日志列表查询，供 Controller 调用。</summary>
public sealed class AccessLogService(IAccessLogDal accessLogs)
{
    /// <summary>查询访问日志列表，透传过滤条件与分页参数。</summary>
    public Task<AccessLogQueryResult> ListAsync(AccessLogQueryRequest request, CancellationToken cancellationToken) =>
        accessLogs.ListAsync(request, cancellationToken);
}
