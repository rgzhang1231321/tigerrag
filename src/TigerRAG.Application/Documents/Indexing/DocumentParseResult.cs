namespace TigerRAG.Application.Documents.Indexing;

/// <summary>文档解析结果：文本内容 + 提取的图片信息。当前阶段图片列表始终为空，后续图片提取阶段填充。</summary>
/// <param name="Content">解析后的纯文本内容。</param>
/// <param name="Images">从文档中提取的图片列表（当前阶段不填充，后续图片提取阶段填充）。</param>
public sealed record DocumentParseResult(
    string Content,
    IReadOnlyList<ExtractedImage> Images);
