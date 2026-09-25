using System.Xml.Linq;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>SVG 解析器：提取 &lt;text&gt;, &lt;tspan&gt;, &lt;textPath&gt; 节点内容。</summary>
internal sealed class SvgParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/svg+xml",
    };

    private static readonly HashSet<string> TextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "tspan", "textPath",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        var doc = XDocument.Load(content);
        foreach (var element in doc.Descendants())
        {
            if (TextElements.Contains(element.Name.LocalName))
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    sb.AppendLine(text);
                }
            }
        }

        return Task.FromResult(sb.ToString().Trim());
    }
}
