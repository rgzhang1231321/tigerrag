namespace TigerRAG.Application.Statistics.Reports;

/// <summary>失败项。</summary>
public sealed record FailureItem(Guid DocumentId, string DocumentTitle, string Reason, DateTimeOffset FailedAt);