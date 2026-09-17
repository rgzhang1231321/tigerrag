namespace TigerRAG.Domain.Documents;

/// <summary>
/// 文档生命周期：Pending → Processing → Indexed / Failed。
/// 合法转换由 <see cref="Document"/> 的领域方法强制。
/// </summary>
public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed
}
