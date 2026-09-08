namespace TigerRAG.Domain.Documents;

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

    public static Document Create(Guid knowledgeBaseId, string fileName, string storagePath) =>
        new(knowledgeBaseId, fileName, storagePath);

    public void StartProcessing()
    {
        EnsureStatus(DocumentStatus.Pending);
        Status = DocumentStatus.Processing;
        FailureReason = null;
    }

    public void CompleteIndexing(int chunkCount)
    {
        EnsureStatus(DocumentStatus.Processing);
        Status = DocumentStatus.Indexed;
        ChunkCount = chunkCount;
        FailureReason = null;
    }

    public void FailIndexing(string reason)
    {
        EnsureStatus(DocumentStatus.Processing);
        Status = DocumentStatus.Failed;
        FailureReason = reason;
    }

    private void EnsureStatus(DocumentStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Document must be {expected} but is {Status}.");
        }
    }
}
