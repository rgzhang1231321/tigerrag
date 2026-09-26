namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>图片解析器：当前阶段返回空内容（图片描述推迟到向量化阶段）。</summary>
internal sealed class ImageParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/bmp",
        "image/tiff",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>返回空内容；图片提取与描述推迟到后续阶段。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
    }
}
