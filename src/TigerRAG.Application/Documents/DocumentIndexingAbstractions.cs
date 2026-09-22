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

/// <summary>原始文件读写端口，由对象存储实现。</summary>
public interface IDocumentFileStorage
{
    /// <summary>读取对象存储中的文件流。</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>写入对象存储；返回是否成功。</summary>
    Task<bool> WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>删除对象存储中的文件；对象不存在视为成功。</summary>
    Task<bool> DeleteAsync(string path, CancellationToken cancellationToken);
}

/// <summary>文档解析端口；按 MIME 派发到 Pdf/Docx/Md/Html/Txt 等具体实现。MIME 用于决定走哪个解析器。</summary>
public interface IDocumentParser
{
    /// <summary>解析文档为纯文本；mimeType 为 null 时按内容探测（不推荐）。</summary>
    Task<string> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken);
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
