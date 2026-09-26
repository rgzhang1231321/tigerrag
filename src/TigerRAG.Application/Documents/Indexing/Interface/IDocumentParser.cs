namespace TigerRAG.Application.Documents.Indexing.Interface;

using TigerRAG.Application.Documents.Indexing;

/// <summary>文档解析端口；按 MIME 派发到 Pdf/Docx/Md/Html/Txt 等具体实现。MIME 用于决定走哪个解析器。</summary>
public interface IDocumentParser
{
    /// <summary>解析文档为结构化结果（文本 + 图片信息）；mimeType 为 null 时按内容探测（不推荐）。</summary>
    Task<DocumentParseResult> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken);
}
