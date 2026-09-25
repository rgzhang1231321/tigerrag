using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>PdfParser 单元测试：UTF-8 兜底读取。</summary>
public sealed class PdfParserTests
{
    private readonly PdfParser _parser = new();

    [Fact]
    public async Task ParseAsync_Utf8Content_ReturnsText()
    {
        var text = "PDF text fallback content 中文";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmpty()
    {
        using var stream = new MemoryStream();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
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
    public void MimeTypes_ContainsApplicationPdf()
    {
        Assert.Contains("application/pdf", _parser.MimeTypes);
    }

    private static MemoryStream ToStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));
}
