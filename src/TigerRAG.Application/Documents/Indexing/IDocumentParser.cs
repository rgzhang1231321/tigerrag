namespace TigerRAG.Application.Documents.Indexing;

/// <summary>文档解析端口；按 MIME 派发到 Pdf/Docx/Md/Html/Txt 等具体实现。MIME 用于决定走哪个解析器。</summary>
public interface IDocumentParser
{
    /// <summary>解析文档为纯文本；mimeType 为 null 时按内容探测（不推荐）。</summary>
    Task<string> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken);
}