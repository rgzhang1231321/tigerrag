namespace TigerRAG.Application.OperationAudit;

/// <summary>审计写入端口（业务埋点调用）。</summary>
public interface IOperationAuditWriter
{
    Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken);
}