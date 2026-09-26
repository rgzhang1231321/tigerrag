using MimeKit;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>EML 解析器：提取邮件正文（纯文本优先，否则 HTML 剥离标签）。</summary>
internal sealed class EmlParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "message/rfc822",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>提取邮件正文（纯文本优先，否则 HTML 剥离标签）。</summary>
    public async Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = await MimeMessage.LoadAsync(content, cancellationToken);

        var text = string.Empty;
        if (message.TextBody is not null)
        {
            text = message.TextBody.Trim();
        }
        else if (message.HtmlBody is not null)
        {
            text = StripHtml(message.HtmlBody).Trim();
        }

        return new DocumentParseResult(text, Array.Empty<ExtractedImage>());
    }

    private static string StripHtml(string html)
    {
        var sb = new System.Text.StringBuilder();
        var inTag = false;
        foreach (var ch in html)
        {
            if (ch == '<')
            {
                inTag = true;
                continue;
            }
            if (ch == '>')
            {
                inTag = false;
                sb.AppendLine();
                continue;
            }
            if (!inTag)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
