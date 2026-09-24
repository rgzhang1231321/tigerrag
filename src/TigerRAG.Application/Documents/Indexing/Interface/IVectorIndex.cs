using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>向量索引端口；写路径按 documentId 先清后写保证幂等；删除路径按 documentId 精确移除。</summary>
public interface IVectorIndex
{
    /// <summary>替换指定文档的全部向量点（先清后写，幂等）。</summary>
    Task ReplaceDocumentAsync(
        Document document,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken);

    /// <summary>删除指定文档的全部向量点；文档不存在视为成功。</summary>
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}