using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents;

public sealed record TextChunk(string Id, string Content, int? PageNumber);

public interface IDocumentRepository
{
    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Document document, CancellationToken cancellationToken);
}

public interface IDocumentFileStorage
{
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
}

public interface IDocumentParser
{
    Task<string> ParseAsync(Stream content, CancellationToken cancellationToken);
}

public interface ITextChunker
{
    IReadOnlyList<TextChunk> Split(string content);
}

public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<TextChunk> chunks,
        CancellationToken cancellationToken);
}

public interface IVectorIndex
{
    Task ReplaceDocumentAsync(
        Document document,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken);
}
