namespace TigerRAG.Infrastructure.Parsing;

/// <summary>PDF 解析器占位：PdfPig 在 .NET 10 项目中存在 TFM 兼容性问题，暂用 UTF-8 兜底。</summary>
internal sealed class PdfParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new System.IO.StreamReader(content, System.Text.Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEndAsync(cancellationToken);
    }
}
