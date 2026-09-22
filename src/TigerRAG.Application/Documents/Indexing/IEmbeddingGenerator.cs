namespace TigerRAG.Application.Documents.Indexing;

/// <summary>Embedding 端口；批大小、维度、超时由具体实现控制。</summary>
public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<TextChunk> chunks,
        CancellationToken cancellationToken);
}