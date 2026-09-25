namespace TigerRAG.Infrastructure.Parsing;

/// <summary>具体解析器接口；每个实现负责一组 MIME 类型的文本提取。</summary>
internal interface ISpecificParser
{
    /// <summary>该解析器支持的 MIME 类型集合（精确匹配，大小写不敏感）。</summary>
    IReadOnlySet<string> MimeTypes { get; }

    /// <summary>从流中提取纯文本。</summary>
    Task<string> ParseAsync(Stream content, CancellationToken cancellationToken);
}
