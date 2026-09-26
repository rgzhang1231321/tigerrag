using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>DOCX 解析器：提取段落和表格文本。</summary>
internal sealed class DocxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 DOCX 中提取段落和表格文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var doc = WordprocessingDocument.Open(content, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
        }

        foreach (var element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
            {
                var text = para.InnerText.Trim();
                if (text.Length > 0)
                {
                    sb.AppendLine(text);
                }
            }
            else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                ExtractTable(table, sb);
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }

    private static void ExtractTable(DocumentFormat.OpenXml.Wordprocessing.Table table, System.Text.StringBuilder sb)
    {
        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                var cellText = cell.InnerText.Trim();
                cells.Add(cellText);
            }
            sb.AppendLine(string.Join("\t", cells));
        }
    }
}

/// <summary>XLSX 解析器：提取工作表单元格文本。</summary>
internal sealed class XlsxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 XLSX 中提取工作表单元格文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var doc = SpreadsheetDocument.Open(content, false);
        var workbook = doc.WorkbookPart?.Workbook;
        if (workbook is null)
        {
            return Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
        }

        var sheetNameMap = new Dictionary<string, string>();
        foreach (var sheet in workbook.Sheets.Cast<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            sheetNameMap[sheet.Id!.Value] = sheet.Name?.Value ?? "Sheet";
        }

        foreach (var sheet in workbook.Sheets.Cast<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheetName = sheet.Name?.Value ?? "Sheet";
            sb.AppendLine($"--- {sheetName} ---");

            var worksheetPart = (WorksheetPart)doc.WorkbookPart!.GetPartById(sheet.Id!.Value);
            var rows = worksheetPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>();
            foreach (var row in rows)
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>())
                {
                    cells.Add(GetCellValue(cell, doc));
                }
                var line = string.Join("\t", cells);
                if (line.Trim().Length > 0)
                {
                    sb.AppendLine(line);
                }
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }

    private static string GetCellValue(DocumentFormat.OpenXml.Spreadsheet.Cell cell, SpreadsheetDocument doc)
    {
        var value = cell.InnerText;
        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString)
        {
            var sharedPart = doc.WorkbookPart?.SharedStringTablePart;
            if (sharedPart is not null && int.TryParse(value, out var index))
            {
                var items = sharedPart.SharedStringTable.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().ToList();
                if (index < items.Count)
                {
                    value = items[index].InnerText;
                }
            }
        }
        return value.Trim();
    }
}

/// <summary>PPTX 解析器：提取幻灯片文本。</summary>
internal sealed class PptxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 PPTX 中提取幻灯片文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var doc = PresentationDocument.Open(content, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null)
        {
            return Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
        }

        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList();
        if (slideIds is null || slideIds.Count == 0)
        {
            return Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
        }

        var first = true;
        foreach (var slideId in slideIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first)
            {
                sb.AppendLine("---");
            }
            first = false;

            var relationshipId = slideId.RelationshipId;
            if (relationshipId is null)
            {
                continue;
            }

            var slidePart = (SlidePart?)presentationPart.GetPartById(relationshipId);
            if (slidePart?.Slide is null)
            {
                continue;
            }

            var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
            foreach (var text in texts)
            {
                var value = text.Text;
                if (value.Length > 0)
                {
                    sb.AppendLine(value);
                }
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }
}
