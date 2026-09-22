using TigerRAG.Application.Shared;

namespace TigerRAG.Application.OperationAudit;

/// <summary>审计查询端口（Controller 调用）。</summary>
public interface IOperationAuditDal
{
    Task<OperationAuditQueryResult> QueryAsync(
        ActorContext actor,
        OperationAuditQueryRequest request,
        CancellationToken cancellationToken);
}