namespace TigerRAG.Application.OperationAudit;

/// <summary>审计列表查询条件。</summary>
public sealed record OperationAuditQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ActorId,
    string? Action,
    string? Keyword,
    int Page,
    int PageSize);