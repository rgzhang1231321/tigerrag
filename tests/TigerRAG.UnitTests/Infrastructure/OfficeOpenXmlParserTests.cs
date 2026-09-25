using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TigerRAG.Infrastructure.Parsing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>DocxParser 单元测试：段落和表格文本提取。</summary>
public sealed class DocxParserTests
{
    private readonly DocxParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleParagraph_ReturnsText()
    {
        using var stream = CreateDocx("Hello World");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Hello World", result);
    }

    [Fact]
    public async Task ParseAsync_MultipleParagraphs_AllReturned()
    {
        using var stream = CreateDocx("First paragraph.", "Second paragraph.");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("First paragraph.", result);
        Assert.Contains("Second paragraph.", result);
    }

    [Fact]
    public async Task ParseAsync_WithTable_ExtractsCellText()
    {
        using var stream = CreateDocxWithTable(new[] { new[] { "Cell1", "Cell2" }, new[] { "Cell3", "Cell4" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Cell1", result);
        Assert.Contains("Cell2", result);
        Assert.Contains("Cell3", result);
        Assert.Contains("Cell4", result);
    }

    [Fact]
    public async Task ParseAsync_EmptyDocument_ReturnsEmpty()
    {
        using var stream = CreateEmptyDocx();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateDocx("你好，世界！");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好，世界！", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateDocx("test");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsDocx()
    {
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document", _parser.MimeTypes);
    }

    private static MemoryStream CreateDocx(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
            foreach (var text in paragraphs)
            {
                body.Append(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(
                        new DocumentFormat.OpenXml.Wordprocessing.Text(text))));
            }
            mainPart.Document.Append(body);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateDocxWithTable(string[][] rows)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();

            var table = new DocumentFormat.OpenXml.Wordprocessing.Table();
            foreach (var rowCells in rows)
            {
                var row = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
                foreach (var cellText in rowCells)
                {
                    row.Append(new DocumentFormat.OpenXml.Wordprocessing.TableCell(
                        new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                            new DocumentFormat.OpenXml.Wordprocessing.Run(
                                new DocumentFormat.OpenXml.Wordprocessing.Text(cellText)))));
                }
                table.Append(row);
            }
            body.Append(table);
            mainPart.Document.Append(body);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateEmptyDocx()
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new DocumentFormat.OpenXml.Wordprocessing.Body());
        }

        ms.Position = 0;
        return ms;
    }
}

/// <summary>XlsxParser 单元测试：工作表单元格文本提取。</summary>
public sealed class XlsxParserTests
{
    private readonly XlsxParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleSheet_ReturnsSheetNameAndCells()
    {
        using var stream = CreateXlsx("Sheet1", new[] { new[] { "A1", "B1" }, new[] { "A2", "B2" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Sheet1", result);
        Assert.Contains("A1", result);
        Assert.Contains("B1", result);
    }

    [Fact]
    public async Task ParseAsync_MultipleSheets_AllReturned()
    {
        using var stream = CreateXlsx(("Sheet1", new[] { new[] { "X" } }), ("Sheet2", new[] { new[] { "Y" } }));
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Sheet1", result);
        Assert.Contains("Sheet2", result);
        Assert.Contains("X", result);
        Assert.Contains("Y", result);
    }

    [Fact]
    public async Task ParseAsync_EmptyWorkbook_ReturnsEmpty()
    {
        using var stream = CreateEmptyXlsx();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreateXlsx("数据", new[] { new[] { "你好", "世界" } });
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好", result);
        Assert.Contains("世界", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreateXlsx("S", new[] { new[] { "v" } });
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsXlsx()
    {
        Assert.Contains("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _parser.MimeTypes);
        Assert.Contains("application/vnd.ms-excel", _parser.MimeTypes);
    }

    private static MemoryStream CreateXlsx(string sheetName, string[][] rows)
    {
        return CreateXlsx((sheetName, rows));
    }

    private static MemoryStream CreateXlsx(params (string name, string[][] rows)[] sheets)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var sheetsElement = new Sheets();
            wbPart.Workbook.Append(sheetsElement);

            uint sheetId = 1;
            foreach (var (name, rows) in sheets)
            {
                var wsPart = wbPart.AddNewPart<WorksheetPart>();
                var ws = new Worksheet();
                var sheetData = new SheetData();

                uint rowIdx = 1;
                foreach (var cells in rows)
                {
                    var row = new Row { RowIndex = rowIdx++ };
                    uint colIdx = 1;
                    foreach (var cellText in cells)
                    {
                        row.Append(new Cell
                        {
                            CellValue = new CellValue(cellText),
                            DataType = CellValues.String,
                        });
                        colIdx++;
                    }
                    sheetData.Append(row);
                }

                ws.Append(sheetData);
                wsPart.Worksheet = ws;

                var sheet = new Sheet
                {
                    Name = name,
                    SheetId = sheetId++,
                    Id = wbPart.GetIdOfPart(wsPart),
                };
                sheetsElement.Append(sheet);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateEmptyXlsx()
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook(new Sheets());
        }

        ms.Position = 0;
        return ms;
    }
}

/// <summary>PptxParser 单元测试：幻灯片文本提取。</summary>
public sealed class PptxParserTests
{
    private readonly PptxParser _parser = new();

    [Fact]
    public async Task ParseAsync_SingleSlide_ReturnsText()
    {
        using var stream = CreatePptx("Slide 1 text");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("Slide 1 text", result);
    }

    [Fact]
    public async Task ParseAsync_MultipleSlides_AllReturned()
    {
        using var stream = CreatePptx("First slide", "Second slide");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("First slide", result);
        Assert.Contains("Second slide", result);
        Assert.Contains("---", result);
    }

    [Fact]
    public async Task ParseAsync_EmptyPresentation_ReturnsEmpty()
    {
        using var stream = CreateEmptyPptx();
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ParseAsync_ChineseContent_Preserved()
    {
        using var stream = CreatePptx("你好幻灯片");
        var result = await _parser.ParseAsync(stream, CancellationToken.None);
        Assert.Contains("你好幻灯片", result);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = CreatePptx("test");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _parser.ParseAsync(stream, cts.Token));
    }

    [Fact]
    public void MimeTypes_ContainsPptx()
    {
        Assert.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation", _parser.MimeTypes);
    }

    private static MemoryStream CreatePptx(params string[] slideTexts)
    {
        var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            // [Content_Types].xml — declare all slides dynamically
            var slideContentTypes = string.Join("\n", slideTexts.Select((t, i) =>
                $@"  <Override PartName=""/ppt/slides/slide{i + 1}.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.slide+xml""/>"));

            WriteZip(archive, "[Content_Types].xml",
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/ppt/presentation.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml""/>
{slideContentTypes}
</Types>");

            // _rels/.rels
            WriteZip(archive, "_rels/.rels",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""ppt/presentation.xml""/>
</Relationships>");

            // ppt/_rels/presentation.xml.rels
            var Enumerable = string.Join("\n", slideTexts.Select((t, i) =>
                $@"  <Relationship Id=""rId{i + 1}"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"" Target=""slides/slide{i + 1}.xml""/>"));

            WriteZip(archive, "ppt/_rels/presentation.xml.rels",
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
{Enumerable}
</Relationships>");

            // ppt/presentation.xml
            var slideRefs = string.Join("\n", slideTexts.Select((t, i) =>
                $@"  <p:sldId id=""{256 + i}"" r:id=""rId{i + 1}""/>"));

            WriteZip(archive, "ppt/presentation.xml",
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:presentation xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:sldIdLst>
{slideRefs}
  </p:sldIdLst>
  <p:sldSz cx=""9144000"" cy=""6858000""/>
</p:presentation>");

            // Slides
            for (var i = 0; i < slideTexts.Length; i++)
            {
                var slideNum = i + 1;
                WriteZip(archive, $"ppt/slides/slide{slideNum}.xml",
                    $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree>
    <p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
    <p:grpSpPr/>
    <p:sp>
      <p:nvSpPr><p:cNvPr id=""2"" name=""TextBox""/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
      <p:spPr/>
      <p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>{slideTexts[i]}</a:t></a:r></a:p></p:txBody>
    </p:sp>
  </p:spTree></p:cSld>
</p:sld>");

                WriteZip(archive, $"ppt/slides/_rels/slide{slideNum}.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
</Relationships>");
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateEmptyPptx()
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
</Types>");

            WriteZip(archive, "_rels/.rels",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""ppt/presentation.xml""/>
</Relationships>");

            WriteZip(archive, "ppt/presentation.xml",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:presentation xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"">
  <p:sldSz cx=""9144000"" cy=""6858000""/>
</p:presentation>");
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
}
