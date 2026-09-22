namespace TigerRAG.Application.OperationAudit;

/// <summary>一条审计记录。</summary>
public sealed record OperationAuditEntry(
    Guid ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string Summary);