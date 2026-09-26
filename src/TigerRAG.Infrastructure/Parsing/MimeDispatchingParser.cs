using System.Text;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>按 MIME 派发到具体解析器。未匹配的 MIME 走 UTF-8 兜底。</summary>
public sealed class MimeDispatchingParser : IDocumentParser
{
    private readonly Dictionary<string, ISpecificParser> _mimeMap = new(StringComparer.OrdinalIgnoreCase);

    public MimeDispatchingParser()
    {
        var parsers = new ISpecificParser[]
        {
            new TextParser(),
            new HtmlParser(),
            new SvgParser(),
            new DocxParser(),
            new XlsxParser(),
            new PptxParser(),
            new PdfParser(),
            new OdtParser(),
            new OdsParser(),
            new OdpParser(),
            new EmlParser(),
            new ImageParser(),
        };

        foreach (var parser in parsers)
        {
            foreach (var mime in parser.MimeTypes)
            {
                _mimeMap[mime] = parser;
            }
        }
    }

    async Task<DocumentParseResult> IDocumentParser.ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mimeType is not null && _mimeMap.TryGetValue(mimeType, out var parser))
        {
            return await parser.ParseAsync(content, cancellationToken);
        }

        // 未匹配或 MIME 未知：UTF-8 兜底。
        using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new DocumentParseResult(text, Array.Empty<ExtractedImage>());
    }
}
