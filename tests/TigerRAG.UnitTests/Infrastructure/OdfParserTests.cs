using System.IO.Compression;
using System.Text;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>OdtParser 单元测试：从 ODF ZIP 中提取 text:p 节点。</summary>
public sealed class OdtParserTests
{
    private readonly OdtParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleParagraph_ReturnsText()
    {
        using var stream = CreateOdf("text:p", "Hello ODT");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello ODT", result.Content);
    }

    [Fact]
    public async Task ParseAsync_MultipleParagraphs_AllReturned()
    {
        using var stream = CreateOdf("text:p", "First para.", "Second para.");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("First para.", result.Content);
        Assert.Contains("Second para.", result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyContent_ReturnsEmpty()
    {
        using var stream = CreateEmptyOdf();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_NoContentXml_ReturnsEmpty()
    {
        using var stream = CreateOdfWithoutContentXml();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateOdf("text:p", "你好 ODT 文档");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好 ODT 文档", result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateOdf("text:p", "test");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsOdt()
    {
        Assert.Contains("application/vnd.oasis.opendocument.text", _parser.MimeTypes);
    }

    private static MemoryStream CreateOdf(string elementName, params string[] texts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""");
        sb.AppendLine(@"  xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"">");
        sb.AppendLine(@"  <office:body><office:text>");
        foreach (var text in texts)
        {
            sb.AppendLine($"    <{elementName}>{text}</{elementName}>");
        }
        sb.AppendLine(@"  </office:text></office:body>");
        sb.AppendLine(@"</office:document-content>");

        return CreateZipWithContentXml(sb.ToString());
    }

    private static MemoryStream CreateEmptyOdf()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"">
  <office:body><office:text></office:text></office:body>
</office:document-content>";
        return CreateZipWithContentXml(xml);
    }

    private static MemoryStream CreateOdfWithoutContentXml()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("mimetype");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("application/vnd.oasis.opendocument.text");
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateZipWithContentXml(string contentXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(contentXml);
        }

        ms.Position = 0;
        return ms;
    }
}

/// <summary>OdsParser 单元测试：从 ODF ZIP 中提取表格行单元格。</summary>
public sealed class OdsParserTests
{
    private readonly OdsParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleRow_ReturnsCellText()
    {
        using var stream = CreateOds(new[] { new[] { "A", "B" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("A", result.Content);
        Assert.Contains("B", result.Content);
    }

    [Fact]
    public async Task ParseAsync_MultipleRows_AllReturned()
    {
        using var stream = CreateOds(new[] { new[] { "R1C1", "R1C2" }, new[] { "R2C1", "R2C2" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("R1C1", result.Content);
        Assert.Contains("R2C2", result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyContent_ReturnsEmpty()
    {
        using var stream = CreateEmptyOds();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateOds(new[] { new[] { "姓名", "年龄" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("姓名", result.Content);
        Assert.Contains("年龄", result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateOds(new[] { new[] { "x" } });
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsOds()
    {
        Assert.Contains("application/vnd.oasis.opendocument.spreadsheet", _parser.MimeTypes);
    }

    private static MemoryStream CreateOds(params string[][] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""");
        sb.AppendLine(@"  xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0""");
        sb.AppendLine(@"  xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"">");
        sb.AppendLine(@"  <office:body><office:spreadsheet>");
        sb.AppendLine(@"    <table:table table:name=""Sheet1"">");
        foreach (var cells in rows)
        {
            sb.AppendLine("      <table:table-row>");
            foreach (var cellText in cells)
            {
                sb.AppendLine($"        <table:table-cell><text:p>{cellText}</text:p></table:table-cell>");
            }
            sb.AppendLine("      </table:table-row>");
        }
        sb.AppendLine(@"    </table:table>");
        sb.AppendLine(@"  </office:spreadsheet></office:body>");
        sb.AppendLine(@"</office:document-content>");

        return CreateZipWithContentXml(sb.ToString());
    }

    private static MemoryStream CreateEmptyOds()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"">
  <office:body><office:spreadsheet></office:spreadsheet></office:body>
</office:document-content>";
        return CreateZipWithContentXml(xml);
    }

    private static MemoryStream CreateZipWithContentXml(string contentXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(contentXml);
        }

        ms.Position = 0;
        return ms;
    }
}

/// <summary>OdpParser 单元测试：从 ODF ZIP 中提取文本框内容。</summary>
public sealed class OdpParserTests
{
    private readonly OdpParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleTextBox_ReturnsText()
    {
        using var stream = CreateOdp("Presentation title");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Presentation title", result.Content);
    }

    [Fact]
    public async Task ParseAsync_MultipleTextBoxes_AllReturned()
    {
        using var stream = CreateOdp("Title slide", "Content slide");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Title slide", result.Content);
        Assert.Contains("Content slide", result.Content);
        Assert.Contains("---", result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyContent_ReturnsEmpty()
    {
        using var stream = CreateEmptyOdp();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateOdp("你好演示文稿");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好演示文稿", result.Content);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateOdp("test");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsOdp()
    {
        Assert.Contains("application/vnd.oasis.opendocument.presentation", _parser.MimeTypes);
    }

    private static MemoryStream CreateOdp(params string[] boxTexts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""");
        sb.AppendLine(@"  xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""");
        sb.AppendLine(@"  xmlns:draw=""urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"">");
        sb.AppendLine(@"  <office:body><office:presentation>");
        foreach (var text in boxTexts)
        {
            sb.AppendLine($"    <draw:page><draw:frame><draw:text-box><text:p>{text}</text:p></draw:text-box></draw:frame></draw:page>");
        }
        sb.AppendLine(@"  </office:presentation></office:body>");
        sb.AppendLine(@"</office:document-content>");

        return CreateZipWithContentXml(sb.ToString());
    }

    private static MemoryStream CreateEmptyOdp()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"">
  <office:body><office:presentation></office:presentation></office:body>
</office:document-content>";
        return CreateZipWithContentXml(xml);
    }

    private static MemoryStream CreateZipWithContentXml(string contentXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(contentXml);
        }

        ms.Position = 0;
        return ms;
    }
}
