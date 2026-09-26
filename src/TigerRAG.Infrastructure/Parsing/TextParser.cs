using System.Text;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>纯文本族解析器：text/plain, text/csv, text/markdown。直接 UTF-8 解码。</summary>
internal sealed class TextParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "text/markdown",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>直接 UTF-8 解码为纯文本。</summary>
    public async Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new DocumentParseResult(text, Array.Empty<ExtractedImage>());
    }
}
