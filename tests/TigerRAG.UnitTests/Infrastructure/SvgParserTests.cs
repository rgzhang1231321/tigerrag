using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>SvgParser 单元测试：提取 text/tspan/textPath 节点文本。</summary>
public sealed class SvgParserTests
{
    private readonly SvgParser _parser = new();

    [Fact]
    public async Task ParseAsync_TextNode_ReturnsText()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""><text>Hello SVG</text></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello SVG", result);
    }

    [Fact]
    public async Task ParseAsync_TspanNode_ReturnsText()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""><text><tspan>Tspan content</tspan></text></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Tspan content", result);
    }

    [Fact]
    public async Task ParseAsync_TextPathNode_ReturnsText()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""><textPath>Path text</textPath></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Path text", result);
    }

    [Fact]
    public async Task ParseAsync_MultipleTextNodes_AllReturned()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg"">
<text>First</text>
<text>Second</text>
</svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("First", result);
        Assert.Contains("Second", result);
    }

    [Fact]
    public async Task ParseAsync_EmptySvg_ReturnsEmpty()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""><text>你好 SVG</text></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好 SVG", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = ToStream("<svg><text>x</text></svg>");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsImageSvgXml()
    {
        Assert.Contains("image/svg+xml", _parser.MimeTypes);
    }

    private static MemoryStream ToStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));
}
