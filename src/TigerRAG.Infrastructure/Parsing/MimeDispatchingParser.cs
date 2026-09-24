using System.Text;
using TigerRAG.Application.Documents.Indexing.Interface;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>按 MIME 派发到具体解析器。当前仅支持纯文本族；PDF/DOCX 等后续按 MIME 前缀接入。</summary>
public sealed class MimeDispatchingParser : IDocumentParser
{
    private static readonly HashSet<string> TextMimePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/",
    };

    public async Task<string> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mimeType is not null && TextMimePrefixes.Any(prefix => mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        // 未提供 MIME 或非文本族：当作 UTF-8 文本读，至少能产出可分块内容供流水线验证。
        using var fallback = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
        return await fallback.ReadToEndAsync(cancellationToken);
    }
}
