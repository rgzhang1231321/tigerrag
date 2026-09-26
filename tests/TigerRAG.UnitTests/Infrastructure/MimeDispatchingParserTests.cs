using System.Text;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>MimeDispatchingParser 单元测试：按 MIME 精确派发到具体解析器。</summary>
public sealed class MimeDispatchingParserTests
{
    private readonly IDocumentParser _parser = new MimeDispatchingParser();

    [Fact]
    public async Task ParseAsync_TextPlain_DecodesUtf8()
    {
        var text = "Hello, 世界！";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/plain", CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_TextMarkdown_DecodesAsText()
    {
        var text = "# Title\n\nSome **markdown** content.";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/markdown", CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_TextCsv_DecodesAsText()
    {
        var text = "a,b,c\n1,2,3";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "text/csv", CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_NullMimeType_FallsBackToUtf8()
    {
        var text = "content with unknown mime";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, null, CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_UnknownMimeType_FallsBackToUtf8()
    {
        var text = "unknown mime type content";
        using var stream = ToStream(text);
        var result = await _parser.ParseAsync(stream, "application/x-unsupported", CancellationToken.None);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyString()
    {
        using var stream = new MemoryStream();
        var result = await _parser.ParseAsync(stream, "text/plain", CancellationToken.None);
        Assert.Equal(string.Empty, result.Content);
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

    [Fact]
    public async Task ParseAsync_PdfMime_RoutesToPdfParser()
    {
        var pdf = CreateMinimalPdf("Real PDF content");
        using var stream = new MemoryStream(pdf);
        var result = await _parser.ParseAsync(stream, "application/pdf", CancellationToken.None);
        Assert.Equal("Real PDF content", result.Content);
    }

    [Fact]
    public async Task ParseAsync_ImagePngMime_RoutesToImageParser()
    {
        using var stream = ToStream("fake-png-bytes");
        var result = await _parser.ParseAsync(stream, "image/png", CancellationToken.None);
        // ImageParser 当前返回空内容。
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ParseAsync_HtmlMime_RoutesToHtmlParser()
    {
        using var stream = ToStream("<p>Hello</p>");
        var result = await _parser.ParseAsync(stream, "text/html", CancellationToken.None);
        Assert.Contains("Hello", result.Content);
        Assert.DoesNotContain("<p>", result.Content);
    }

    [Fact]
    public async Task ParseAsync_SvgMime_RoutesToSvgParser()
    {
        var svg = @"<svg xmlns=""http://www.w3.org/2000/svg""><text>SVG text</text></svg>";
        using var stream = ToStream(svg);
        var result = await _parser.ParseAsync(stream, "image/svg+xml", CancellationToken.None);
        Assert.Contains("SVG text", result.Content);
    }

    [Fact]
    public async Task ParseAsync_EmlMime_RoutesToEmlParser()
    {
        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress("Test", "test@example.com"));
        message.To.Add(new MimeKit.MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Test";
        message.Body = new MimeKit.TextPart("plain") { Text = "EML body text" };

        var ms = new MemoryStream();
        message.WriteTo(ms);
        ms.Position = 0;

        var result = await _parser.ParseAsync(ms, "message/rfc822", CancellationToken.None);
        Assert.Contains("EML body text", result.Content);
    }

    [Fact]
    public async Task ParseAsync_DocxMime_RoutesToDocxParser()
    {
        var docxStream = CreateMinimalDocx("Hello DOCX");
        var result = await _parser.ParseAsync(docxStream, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", CancellationToken.None);
        Assert.Contains("Hello DOCX", result.Content);
    }

    [Fact]
    public async Task ParseAsync_XlsxMime_RoutesToXlsxParser()
    {
        var xlsxStream = CreateMinimalXlsx();
        var result = await _parser.ParseAsync(xlsxStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", CancellationToken.None);
        Assert.Contains("Sheet1", result.Content);
        Assert.Contains("CellA1", result.Content);
    }

    [Fact]
    public async Task ParseAsync_PptxMime_RoutesToPptxParser()
    {
        var pptxStream = CreateMinimalPptx("Slide text");
        var result = await _parser.ParseAsync(pptxStream, "application/vnd.openxmlformats-officedocument.presentationml.presentation", CancellationToken.None);
        Assert.Contains("Slide text", result.Content);
    }

    [Fact]
    public async Task ParseAsync_OdtMime_RoutesToOdtParser()
    {
        var odfStream = CreateMinimalOdf("Hello ODT");
        var result = await _parser.ParseAsync(odfStream, "application/vnd.oasis.opendocument.text", CancellationToken.None);
        Assert.Contains("Hello ODT", result.Content);
    }

    [Fact]
    public async Task ParseAsync_AllResults_HaveEmptyImagesList()
    {
        // 当前阶段所有解析器的 Images 列表应为空。
        using var stream = ToStream("test content");
        var result = await _parser.ParseAsync(stream, "text/plain", CancellationToken.None);
        Assert.Empty(result.Images);
    }

    private static MemoryStream ToStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));

    /// <summary>用 PdfPig Writer 构建单页文本 PDF。</summary>
    private static byte[] CreateMinimalPdf(string text)
    {
        using var ms = new MemoryStream();
        using var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder(ms);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4, true);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        return builder.Build();
    }

    private static MemoryStream CreateMinimalDocx(string text)
    {
        var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
            body.Append(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(text))));
            mainPart.Document.Append(body);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateMinimalXlsx()
    {
        var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(
            ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
            var sheets = new DocumentFormat.OpenXml.Spreadsheet.Sheets();
            wbPart.Workbook.Append(sheets);

            var wsPart = wbPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
            var ws = new DocumentFormat.OpenXml.Spreadsheet.Worksheet();
            var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
            var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
            row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
            {
                CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("CellA1"),
                DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
            });
            sheetData.Append(row);
            ws.Append(sheetData);
            wsPart.Worksheet = ws;

            var sheet = new DocumentFormat.OpenXml.Spreadsheet.Sheet
            {
                Name = "Sheet1",
                SheetId = 1,
                Id = wbPart.GetIdOfPart(wsPart),
            };
            sheets.Append(sheet);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateMinimalPptx(string text)
    {
        var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            WriteZip(archive, "[Content_Types].xml",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/ppt/presentation.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml""/>
  <Override PartName=""/ppt/slides/slide1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.slide+xml""/>
</Types>");

            WriteZip(archive, "_rels/.rels",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""ppt/presentation.xml""/>
</Relationships>");

            WriteZip(archive, "ppt/_rels/presentation.xml.rels",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"" Target=""slides/slide1.xml""/>
</Relationships>");

            WriteZip(archive, "ppt/presentation.xml",
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:presentation xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:sldIdLst>
    <p:sldId id=""256"" r:id=""rId1""/>
  </p:sldIdLst>
  <p:sldSz cx=""9144000"" cy=""6858000""/>
</p:presentation>");

            WriteZip(archive, "ppt/slides/slide1.xml",
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree>
    <p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
    <p:grpSpPr/>
    <p:sp>
      <p:nvSpPr><p:cNvPr id=""2"" name=""TextBox""/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
      <p:spPr/>
      <p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody>
    </p:sp>
  </p:spTree></p:cSld>
</p:sld>");

            WriteZip(archive, "ppt/slides/_rels/slide1.xml.rels",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
</Relationships>");
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteZip(System.IO.Compression.ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static MemoryStream CreateMinimalOdf(string text)
    {
        var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
  xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"">
  <office:body><office:text><text:p>{text}</text:p></office:text></office:body>
</office:document-content>";
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(xml);
        }

        ms.Position = 0;
        return ms;
    }
}
