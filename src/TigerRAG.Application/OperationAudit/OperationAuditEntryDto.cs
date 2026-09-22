namespace TigerRAG.Application.OperationAudit;

/// <summary>审计列表单项。</summary>
public sealed record OperationAuditEntryDto(
    long Id,
    DateTimeOffset CreatedAt,
    Guid ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string Summary);