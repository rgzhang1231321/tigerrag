using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents;

/// <summary>索引流水线用的最小文本片段。</summary>
public sealed record TextChunk(string Id, string Content, int? PageNumber);

/// <summary>文档持久化端口；Application 不感知 EF Core。</summary>
public interface IDocumentRepository
{
    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Document document, CancellationToken cancellationToken);
}

/// <summary>原始文件读取端口，由对象存储实现。</summary>
public interface IDocumentFileStorage
{
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
}

/// <summary>文档解析端口；按 MIME 派发到 Pdf/Docx/Md/Html/Txt 等具体实现。</summary>
public interface IDocumentParser
{
    Task<string> ParseAsync(Stream content, CancellationToken cancellationToken);
}

/// <summary>文本分块端口；典型策略为 500 token 窗口 + 50 token 重叠。</summary>
public interface ITextChunker
{
    IReadOnlyList<TextChunk> Split(string content);
}

/// <summary>Embedding 端口；批大小、维度、超时由具体实现控制。</summary>
public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<TextChunk> chunks,
        CancellationToken cancellationToken);
}

/// <summary>向量索引端口；先按 documentId 清理再写入，保证幂等。</summary>
public interface IVectorIndex
{
    Task ReplaceDocumentAsync(
        Document document,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken);
}
