using System.Text;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>ImageParser 单元测试：依赖 IImageDescriptor 生成描述。</summary>
public sealed class ImageParserTests
{
    [Fact]
    public async Task ParseAsync_WithDescriptor_ReturnsDescription()
    {
        var descriptor = new StubImageDescriptor("A beautiful landscape");
        var parser = new ImageParser(descriptor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal("A beautiful landscape", result);
    }

    [Fact]
    public async Task ParseAsync_WithoutDescriptor_ReturnsNotConfigured()
    {
        var parser = new ImageParser(null);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal("[图片: 未配置描述服务]", result);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmpty()
    {
        var descriptor = new StubImageDescriptor("should not be called");
        var parser = new ImageParser(descriptor);
        using var stream = new MemoryStream();

        var result = await parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_DescriptorThrows_ReturnsFailureMessage()
    {
        var descriptor = new ThrowingImageDescriptor();
        var parser = new ImageParser(descriptor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal("[图片: 描述失败]", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new ImageParser(new StubImageDescriptor("unused"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsCommonImageTypes()
    {
        var parser = new ImageParser(null);
        Assert.Contains("image/png", parser.MimeTypes);
        Assert.Contains("image/jpeg", parser.MimeTypes);
        Assert.Contains("image/gif", parser.MimeTypes);
        Assert.Contains("image/webp", parser.MimeTypes);
        Assert.Contains("image/bmp", parser.MimeTypes);
        Assert.Contains("image/tiff", parser.MimeTypes);
    }

    /// <summary>测试用的固定描述实现。</summary>
    private sealed class StubImageDescriptor : IImageDescriptor
    {
        private readonly string _description;

        public StubImageDescriptor(string description)
        {
            _description = description;
        }

        public Task<string> DescribeAsync(
            ReadOnlyMemory<byte> imageBytes,
            string mimeType,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_description);
        }
    }

    /// <summary>测试用的抛异常描述实现。</summary>
    private sealed class ThrowingImageDescriptor : IImageDescriptor
    {
        public Task<string> DescribeAsync(
            ReadOnlyMemory<byte> imageBytes,
            string mimeType,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("LLM service unavailable");
        }
    }
}
