using System.Text;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>ImageParser 单元测试：当前阶段返回空内容，后续图片提取阶段扩展。</summary>
public sealed class ImageParserTests
{
    [Fact]
    public async Task ParseAsync_ReturnsEmptyContent()
    {
        var parser = new ImageParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal(string.Empty, result.Content);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyContent()
    {
        var parser = new ImageParser();
        using var stream = new MemoryStream();

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new ImageParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsCommonImageTypes()
    {
        var parser = new ImageParser();
        Assert.Contains("image/png", parser.MimeTypes);
        Assert.Contains("image/jpeg", parser.MimeTypes);
        Assert.Contains("image/gif", parser.MimeTypes);
        Assert.Contains("image/webp", parser.MimeTypes);
        Assert.Contains("image/bmp", parser.MimeTypes);
        Assert.Contains("image/tiff", parser.MimeTypes);
    }
}
