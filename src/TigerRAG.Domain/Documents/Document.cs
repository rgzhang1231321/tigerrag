namespace TigerRAG.Domain.Documents;

/// <summary>
/// 文档聚合根。所有状态变更必须通过领域方法，确保状态机不变量集中维护。
/// </summary>
public sealed class Document
{
    private Document(Guid knowledgeBaseId, string fileName, string storagePath)
    {
        Id = Guid.NewGuid();
        KnowledgeBaseId = knowledgeBaseId;
        FileName = fileName;
        StoragePath = storagePath;
        Status = DocumentStatus.Pending;
    }

    public Guid Id { get; }
    public Guid KnowledgeBaseId { get; }
    public string FileName { get; }
    public string StoragePath { get; }
    public DocumentStatus Status { get; private set; }
    public int ChunkCount { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>工厂方法：创建一个 Pending 状态的文档。</summary>
    public static Document Create(Guid knowledgeBaseId, string fileName, string storagePath) =>
        new(knowledgeBaseId, fileName, storagePath);

    /// <summary>Pending → Processing；Worker 领取任务时调用。</summary>
    public void StartProcessing()
    {
        EnsureStatus(DocumentStatus.Pending);
        Status = DocumentStatus.Processing;
        FailureReason = null;
    }

    /// <summary>Processing → Indexed，并记录分块数量。</summary>
    public void CompleteIndexing(int chunkCount)
    {
        EnsureStatus(DocumentStatus.Processing);
        Status = DocumentStatus.Indexed;
        ChunkCount = chunkCount;
        FailureReason = null;
    }

    /// <summary>Processing → Failed，保留错误原因供运维排查。</summary>
    public void FailIndexing(string reason)
    {
        EnsureStatus(DocumentStatus.Processing);
        Status = DocumentStatus.Failed;
        FailureReason = reason;
    }

    // 状态机的不变量：仅允许从上述方法进入新状态；非法转换通过抛异常显式失败。
    private void EnsureStatus(DocumentStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Document must be {expected} but is {Status}.");
        }
    }
}
