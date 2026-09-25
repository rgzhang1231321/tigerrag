using HtmlAgilityPack;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>HTML 解析器：剥离标签，保留文本内容和换行语义。</summary>
internal sealed class HtmlParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "div", "h1", "h2", "h3", "h4", "h5", "h6",
        "li", "tr", "td", "th", "blockquote", "pre", "section",
        "article", "header", "footer", "table", "ul", "ol", "hr",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doc = new HtmlDocument();
        doc.Load(content);
        return Task.FromResult(ExtractText(doc.DocumentNode));
    }

    private static string ExtractText(HtmlNode node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            AppendNodeText(child, sb);
        }
        return sb.ToString().Trim();
    }

    private static void AppendNodeText(HtmlNode node, System.Text.StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(node.InnerText);
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return;
        }

        var name = node.Name.ToLowerInvariant();

        // 脚本和样式内容直接跳过。
        if (name == "script" || name == "style")
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendNodeText(child, sb);
        }

        // 块级元素后追加换行。
        if (BlockTags.Contains(name))
        {
            sb.AppendLine();
        }
    }
}
