using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>MimeDispatchingParser 单元测试：text/ 前缀走文本解析，其他/空 MIME 走 UTF-8 兜底。</summary>
public sealed class MimeDispatchingParserTests
{
    private readonly MimeDispatchingParser _parser = new();

    [Fact]
    public async Task ParseAsync_TextPlain_DecodesUtf8()
    {
        var text = "Hello, 世界！";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/plain", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_TextMarkdown_DecodesAsText()
    {
        var text = "# Title\n\nSome **markdown** content.";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/markdown", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_TextCsv_DecodesAsText()
    {
        var text = "a,b,c\n1,2,3";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/csv", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_ApplicationPdf_FallsBackToUtf8()
    {
        // 当前未实现 PDF 解析器：按 UTF-8 兜底读取（至少不抛异常）。
        var text = "PDF binary content as utf8 fallback";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "application/pdf", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_ApplicationDocx_FallsBackToUtf8()
    {
        var text = "DOCX binary content as utf8 fallback";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_NullMimeType_FallsBackToUtf8()
    {
        var text = "content with unknown mime";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, null, CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyString()
    {
        using var stream = new MemoryStream();
        var result = await _parser.ParseAsync(stream, "text/plain", CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_TextPlainWithCharset_StillMatchesTextPrefix()
    {
        // text/plain; charset=utf-8 以 text/ 开头，走文本解析。
        var text = "charset test content";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/plain; charset=utf-8", CancellationToken.None);
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = ToStream("data");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, "text/plain", cts.Token));
    }

    private static MemoryStream ToStream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
