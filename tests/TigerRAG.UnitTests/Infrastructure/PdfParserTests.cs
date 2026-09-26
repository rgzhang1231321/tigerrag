using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>PdfParser 单元测试：PdfPig 按页提取文本。</summary>
public sealed class PdfParserTests
{
    /// <summary>1x1 PNG，用于构造带嵌入图片的测试 PDF。</summary>
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task ParseAsync_SinglePagePdf_ExtractsText()
    {
        var parser = new PdfParser();
        using var stream = ToStream(CreatePdf("Hello PDF world"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal("Hello PDF world", result.Content);
    }

    [Fact]
    public async Task ParseAsync_MultiPagePdf_JoinsPagesWithBlankLine()
    {
        var parser = new PdfParser();
        using var stream = ToStream(CreatePdf("First page", "Second page"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal("First page\n\nSecond page", result.Content);
    }

    [Fact]
    public async Task ParseAsync_TextlessPage_ReturnsEmpty()
    {
        var parser = new PdfParser();
        using var stream = ToStream(CreatePdf(""));

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmbeddedImage_DoesNotInlinDescription()
    {
        // 当前阶段 PdfParser 不做图片描述，仅提取文本。
        var parser = new PdfParser();
        using var stream = ToStream(CreatePdfWithImage("Page with image"));

        var result = await parser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal("Page with image", result.Content);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ParseAsync_CorruptBytes_ThrowsPdfDocumentFormatException()
    {
        var parser = new PdfParser();
        using var stream = ToStream("this is not a pdf"u8.ToArray());

        await Assert.ThrowsAsync<PdfDocumentFormatException>(
            () => parser.ParseAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ThrowsPdfDocumentFormatException()
    {
        var parser = new PdfParser();
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<PdfDocumentFormatException>(
            () => parser.ParseAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new PdfParser();
        using var stream = ToStream(CreatePdf("data"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsApplicationPdf()
    {
        var parser = new PdfParser();
        Assert.Contains("application/pdf", parser.MimeTypes);
    }

    /// <summary>用 PdfPig Writer 构建多页测试 PDF；空字符串页生成无文本页。</summary>
    private static byte[] CreatePdf(params string[] pageTexts)
    {
        using var ms = new MemoryStream();
        using var builder = new PdfDocumentBuilder(ms);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4, true);
            if (text.Length > 0)
            {
                page.AddText(text, 12, new PdfPoint(50, 700), font);
            }
        }
        return builder.Build();
    }

    /// <summary>构建单页带一张嵌入 PNG 的测试 PDF。</summary>
    private static byte[] CreatePdfWithImage(string pageText)
    {
        using var ms = new MemoryStream();
        using var builder = new PdfDocumentBuilder(ms);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4, true);
        if (pageText.Length > 0)
        {
            page.AddText(pageText, 12, new PdfPoint(50, 700), font);
        }
        page.AddPng(Png1x1, new PdfRectangle(50, 600, 350, 650));
        return builder.Build();
    }

    private static MemoryStream ToStream(byte[] bytes) => new(bytes);
}
