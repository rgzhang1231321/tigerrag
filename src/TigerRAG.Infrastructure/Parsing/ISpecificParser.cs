namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>具体解析器接口；每个实现负责一组 MIME 类型的文本提取。</summary>
internal interface ISpecificParser
{
    /// <summary>该解析器支持的 MIME 类型集合（精确匹配，大小写不敏感）。</summary>
    IReadOnlySet<string> MimeTypes { get; }

    /// <summary>从流中提取文本和图片信息。当前阶段图片列表始终为空，后续图片提取阶段填充。</summary>
    Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken);
}
