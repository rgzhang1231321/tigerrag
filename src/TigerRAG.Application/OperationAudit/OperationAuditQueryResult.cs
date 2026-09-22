namespace TigerRAG.Application.OperationAudit;

/// <summary>审计列表查询结果。</summary>
public sealed record OperationAuditQueryResult(
    IReadOnlyList<OperationAuditEntryDto> Entries,
    int Total);