using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>HtmlParser 单元测试：剥离标签，保留文本和换行语义。</summary>
public sealed class HtmlParserTests
{
    private readonly HtmlParser _parser = new();

    [Fact]
    public async Task ParseAsync_SimpleParagraph_ReturnsText()
    {
        var html = "<p>Hello World</p>";
        using var stream = ToStream(html);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello World", result);
        Assert.DoesNotContain("<p>", result);
    }

    [Fact]
    public async Task ParseAsync_MultipleParagraphs_PreservesLineBreaks()
    {
        var html = "<p>First paragraph.</p><p>Second paragraph.</p>";
        using var stream = ToStream(html);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("First paragraph.", result);
        Assert.Contains("Second paragraph.", result);
    }

    [Fact]
    public async Task ParseAsync_HeadingAndList_ReturnsText()
    {
        var html = "<h1>Title</h1><ul><li>Item 1</li><li>Item 2</li></ul>";
        using var stream = ToStream(html);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Title", result);
        Assert.Contains("Item 1", result);
        Assert.Contains("Item 2", result);
    }

    [Fact]
    public async Task ParseAsync_ScriptAndStyle_Ignored()
    {
        var html = "<style>body{color:red}</style><p>Visible text</p><script>alert('x')</script>";
        using var stream = ToStream(html);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Visible text", result);
        Assert.DoesNotContain("body{color:red}", result);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public async Task ParseAsync_EmptyHtml_ReturnsEmpty()
    {
        using var stream = ToStream("<html><head></head><body></body></html>");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        var html = "<p>你好，世界！</p>";
        using var stream = ToStream(html);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好，世界！", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = ToStream("<p>test</p>");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsTextHtml()
    {
        Assert.Contains("text/html", _parser.MimeTypes);
    }

    private static MemoryStream ToStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));
}
