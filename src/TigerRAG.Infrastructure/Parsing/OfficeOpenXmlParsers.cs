using NPOI.XWPF.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using SSModel = NPOI.SS.UserModel;

namespace TigerRAG.Infrastructure.Parsing;

using TigerRAG.Application.Documents.Indexing;

/// <summary>DOCX 解析器（NPOI XWPF）：提取段落和表格文本。</summary>
internal sealed class DocxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 DOCX 中提取段落、表格文本和嵌入图片，并记录图片在文本中的字符偏移量。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();
        var images = new List<ExtractedImage>();

        using var doc = new XWPFDocument(content);
        foreach (var bodyElement in doc.BodyElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (bodyElement)
            {
                case XWPFParagraph para:
                    var paraText = para.Text.Trim();
                    if (paraText.Length > 0)
                    {
                        sb.AppendLine(paraText);
                    }
                    ExtractParagraphImages(para, sb.Length, images);
                    break;
                case XWPFTable table:
                    ExtractTable(table, sb);
                    break;
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), images));
    }

    /// <summary>从段落中提取嵌入的图片，Position 为图片所在段落在文本中的起始字符偏移量。</summary>
    private static void ExtractParagraphImages(XWPFParagraph para, int paragraphOffset, List<ExtractedImage> images)
    {
        foreach (var run in para.Runs)
        {
            foreach (var picture in run.GetEmbeddedPictures())
            {
                var pictureData = picture.GetPictureData();
                var imageBytes = pictureData?.Data;
                if (imageBytes is not null && imageBytes.Length > 0)
                {
                    images.Add(new ExtractedImage(imageBytes, paragraphOffset));
                }
            }
        }
    }

    private static void ExtractTable(XWPFTable table, System.Text.StringBuilder sb)
    {
        foreach (var row in table.Rows)
        {
            var cells = new List<string>();
            foreach (var cell in row.GetTableCells())
            {
                var cellText = cell.GetText().Trim();
                cells.Add(cellText);
            }
            sb.AppendLine(string.Join("\t", cells));
        }
    }
}

/// <summary>XLSX 解析器（NPOI XSSF）：将工作表转换为 Markdown 表格。</summary>
internal sealed class XlsxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 XLSX 中提取工作表并转换为 Markdown 表格。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var workbook = new XSSFWorkbook(content);
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = workbook.GetSheetAt(i);
            AppendSheetToMarkdown(sheet, sb);
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }

    private static void AppendSheetToMarkdown(SSModel.ISheet sheet, System.Text.StringBuilder sb)
    {
        sb.AppendLine($"## {sheet.SheetName}");
        sb.AppendLine();

        var firstRowNum = sheet.FirstRowNum;
        var lastRowNum = sheet.LastRowNum;
        if (firstRowNum > lastRowNum)
        {
            sb.AppendLine();
            return;
        }

        // 确定列数
        var maxCols = 0;
        for (var r = firstRowNum; r <= lastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is not null && row.LastCellNum > maxCols)
            {
                maxCols = row.LastCellNum;
            }
        }
        if (maxCols == 0)
        {
            sb.AppendLine();
            return;
        }

        // 表头
        var headerRow = sheet.GetRow(firstRowNum);
        sb.Append("|");
        for (var c = 0; c < maxCols; c++)
        {
            sb.Append($" {GetCellValue(headerRow?.GetCell(c))} |");
        }
        sb.AppendLine();

        // 分隔行
        sb.Append("|");
        for (var c = 0; c < maxCols; c++)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // 数据行
        for (var r = firstRowNum + 1; r <= lastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            sb.Append("|");
            for (var c = 0; c < maxCols; c++)
            {
                sb.Append($" {GetCellValue(row.GetCell(c))} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
    }

    private static string GetCellValue(SSModel.ICell cell)
    {
        if (cell is null) return string.Empty;
        return cell.CellType switch
        {
            SSModel.CellType.String => EscapeMarkdown(cell.StringCellValue.Trim()),
            SSModel.CellType.Numeric => SSModel.DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? string.Empty
                : cell.NumericCellValue.ToString(),
            SSModel.CellType.Boolean => cell.BooleanCellValue.ToString(),
            SSModel.CellType.Formula => EscapeMarkdown(cell!.ToString().Trim()),
            _ => string.Empty,
        };
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|").Replace("\n", "<br>");
    }
}

/// <summary>XLS 解析器（NPOI HSSF）：将工作表转换为 Markdown 表格。</summary>
internal sealed class XlsParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-excel",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 XLS（Excel 97-2003 二进制格式）中提取工作表并转换为 Markdown 表格。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        // HSSFWorkbook 需要可随机读取的流，先复制到内存。
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        ms.Position = 0;

        using var workbook = new HSSFWorkbook(ms);
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = workbook.GetSheetAt(i);
            AppendSheetToMarkdown(sheet, sb);
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
    }

    private static void AppendSheetToMarkdown(SSModel.ISheet sheet, System.Text.StringBuilder sb)
    {
        sb.AppendLine($"## {sheet.SheetName}");
        sb.AppendLine();

        var firstRowNum = sheet.FirstRowNum;
        var lastRowNum = sheet.LastRowNum;
        if (firstRowNum > lastRowNum)
        {
            sb.AppendLine();
            return;
        }

        // 确定列数
        var maxCols = 0;
        for (var r = firstRowNum; r <= lastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is not null && row.LastCellNum > maxCols)
            {
                maxCols = row.LastCellNum;
            }
        }
        if (maxCols == 0)
        {
            sb.AppendLine();
            return;
        }

        // 表头
        var headerRow = sheet.GetRow(firstRowNum);
        sb.Append("|");
        for (var c = 0; c < maxCols; c++)
        {
            sb.Append($" {GetCellValue(headerRow?.GetCell(c))} |");
        }
        sb.AppendLine();

        // 分隔行
        sb.Append("|");
        for (var c = 0; c < maxCols; c++)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // 数据行
        for (var r = firstRowNum + 1; r <= lastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            sb.Append("|");
            for (var c = 0; c < maxCols; c++)
            {
                sb.Append($" {GetCellValue(row.GetCell(c))} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
    }

    private static string GetCellValue(SSModel.ICell cell)
    {
        if (cell is null) return string.Empty;
        return cell.CellType switch
        {
            SSModel.CellType.String => EscapeMarkdown(cell.StringCellValue.Trim()),
            SSModel.CellType.Numeric => SSModel.DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? string.Empty
                : cell.NumericCellValue.ToString(),
            SSModel.CellType.Boolean => cell.BooleanCellValue.ToString(),
            SSModel.CellType.Formula => EscapeMarkdown(cell!.ToString().Trim()),
            _ => string.Empty,
        };
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|").Replace("\n", "<br>");
    }
}
