using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>TextParser 单元测试：text/plain, text/csv, text/markdown 直接 UTF-8 解码。</summary>
public sealed class TextParserTests
{
    private readonly TextParser _parser = new();

    [Fact]
    public async Task ParseAsync_PlainText_ReturnsContent()
    {
        var text = "Hello, 世界！";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_Csv_ReturnsContent()
    {
        var text = "name,age\nAlice,30\nBob,25";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_Markdown_ReturnsContent()
    {
        var text = "# Title\n\nSome **bold** text.";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmpty()
    {
        using var stream = new MemoryStream();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = ToStream("data");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsTextPlainCsvMarkdown()
    {
        Assert.Contains("text/plain", _parser.MimeTypes);
        Assert.Contains("text/csv", _parser.MimeTypes);
        Assert.Contains("text/markdown", _parser.MimeTypes);
    }

    private static MemoryStream ToStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));
}
