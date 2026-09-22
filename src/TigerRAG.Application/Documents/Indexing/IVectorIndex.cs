using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents.Indexing;

/// <summary>向量索引端口；先按 documentId 清理再写入，保证幂等。</summary>
public interface IVectorIndex
{
    Task ReplaceDocumentAsync(
        Document document,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken);
}