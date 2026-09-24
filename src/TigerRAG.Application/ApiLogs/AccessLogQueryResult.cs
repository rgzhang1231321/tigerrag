namespace TigerRAG.Application.ApiLogs;

/// <summary>访问日志查询结果：分页列表 + 总数。</summary>
public sealed record AccessLogQueryResult(
    IReadOnlyList<AccessLogEntryDto> Entries,
    int Total);
