using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>文档持久化端口；Application 不感知 EF Core。</summary>
public interface IDocumentRepository
{
    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Document document, CancellationToken cancellationToken);
}