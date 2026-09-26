using System.Xml.Linq;
using System.IO.Compression;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>PPTX 解析器：从 ZIP 包中提取幻灯片文本。</summary>
internal sealed class PptxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 PPTX 中提取幻灯片文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < slideEntries.Count; i++)
        {
            var entry = slideEntries[i];
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0)
            {
                sb.AppendLine("---");
            }
            using var entryStream = entry.Open();
            var doc = XDocument.Load(entryStream);
            var texts = doc.Descendants()
                .Where(e => e.Name.LocalName == "t")
                .Select(e => e.Value.Trim())
                .Where(t => t.Length > 0);

            foreach (var text in texts)
            {
                sb.AppendLine(text);
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }
}
