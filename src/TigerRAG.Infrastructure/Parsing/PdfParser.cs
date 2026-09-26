using UglyToad.PdfPig;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>PDF 解析器：PdfPig 按页提取文本。图片提取与描述推迟到后续阶段。</summary>
internal sealed class PdfParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 PDF 中提取纯文本内容；图片提取推迟到后续阶段。</summary>
    public async Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // PdfPig 需要可随机读取的流；对象存储返回的流不保证可寻址，先复制到内存。
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        // GetBuffer 切片避免整份再拷贝一次；空流与损坏 PDF 统一抛 PdfDocumentFormatException。
        var bytes = new ReadOnlyMemory<byte>(ms.GetBuffer(), 0, (int)ms.Length);

        // 损坏或加密的 PDF 由 PdfPig 抛出，上游 DocumentIndexingService 捕获后标记文档 Failed。
        using var document = PdfDocument.Open(bytes);
        var pageTexts = new List<string>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageTexts.Add(page.Text.Trim());
        }

        var text = string.Join("\n\n", pageTexts).Trim();
        return new DocumentParseResult(text, Array.Empty<ExtractedImage>());
    }
}
