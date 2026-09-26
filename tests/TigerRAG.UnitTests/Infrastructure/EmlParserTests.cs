using System.Text;
using MimeKit;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>EmlParser 单元测试：纯文本正文优先，HTML 剥离为回退。</summary>
public sealed class EmlParserTests
{
    private readonly EmlParser _parser = new();

    [Fact]
    public async Task ParseAsync_PlainTextBody_ReturnsText()
    {
        using var stream = CreateEml("plain", "Hello email body");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello email body", result.Content);
    }

    [Fact]
    public async Task ParseAsync_HtmlBody_StripsTags()
    {
        using var stream = CreateEml("html", "<p>Hello <b>World</b></p>");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello", result.Content);
        Assert.Contains("World", result.Content);
        Assert.DoesNotContain("<p>", result.Content);
        Assert.DoesNotContain("<b>", result.Content);
    }

    [Fact]
    public async Task ParseAsync_BothBodies_PrefersTextBody()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Test";
        message.Body = new Multipart("alternative")
        {
            new TextPart("plain") { Text = "Plain text version" },
            new TextPart("html") { Text = "<p>HTML version</p>" },
        };

        using var stream = ToStream(message);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Plain text version", result.Content);
        Assert.DoesNotContain("HTML version", result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyMessage_ReturnsEmpty()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "No body";

        using var stream = ToStream(message);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateEml("plain", "你好邮件内容");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好邮件内容", result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateEml("plain", "test");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsMessageRfc822()
    {
        Assert.Contains("message/rfc822", _parser.MimeTypes);
    }

    private static MemoryStream CreateEml(string bodyKind, string bodyText)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Test subject";

        message.Body = bodyKind switch
        {
            "plain" => new TextPart("plain") { Text = bodyText },
            "html" => new TextPart("html") { Text = bodyText },
            _ => new TextPart("plain") { Text = bodyText },
        };

        return ToStream(message);
    }

    private static MemoryStream ToStream(MimeMessage message)
    {
        var ms = new MemoryStream();
        message.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }
}
