namespace TigerRAG.Application.ApiLogs;

/// <summary>日志查询结果：分页列表 + 总数。</summary>
public sealed record ApiLogQueryResult(
    IReadOnlyList<ApiLogEntryDto> Entries,
    int Total);