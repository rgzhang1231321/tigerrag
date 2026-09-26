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

    /// <summary>从 DOCX 中提取段落和表格文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

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
                    break;
                case XWPFTable table:
                    ExtractTable(table, sb);
                    break;
            }
        }

        return Task.FromResult(new DocumentParseResult(sb.ToString().Trim(), Array.Empty<ExtractedImage>()));
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

/// <summary>XLSX 解析器（NPOI XSSF）：提取工作表单元格文本。</summary>
internal sealed class XlsxParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 XLSX 中提取工作表单元格文本。</summary>
    public Task<DocumentParseResult> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new System.Text.StringBuilder();

        using var workbook = new XSSFWorkbook(content);
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = workbook.GetSheetAt(i);
            var sheetName = sheet.SheetName;
            sb.AppendLine($"--- {sheetName} ---");

            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row is null) continue;
                var cells = new List<string>();
                foreach (var cell in row)
                {
                    cells.Add(GetCellValue(cell));
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

    private static string GetCellValue(SSModel.ICell cell)
    {
        if (cell is null) return string.Empty;
        return cell.CellType switch
        {
            SSModel.CellType.String => cell.StringCellValue.Trim(),
            SSModel.CellType.Numeric => SSModel.DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? string.Empty
                : cell.NumericCellValue.ToString(),
            SSModel.CellType.Boolean => cell.BooleanCellValue.ToString(),
            SSModel.CellType.Formula => cell!.ToString().Trim(),
            _ => string.Empty,
        };
    }
}

/// <summary>XLS 解析器（NPOI HSSF）：提取工作表单元格文本。</summary>
internal sealed class XlsParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-excel",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    /// <summary>从 XLS（Excel 97-2003 二进制格式）中提取工作表单元格文本。</summary>
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
            var sheetName = sheet.SheetName;
            sb.AppendLine($"--- {sheetName} ---");

            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row is null) continue;
                var cells = new List<string>();
                foreach (var cell in row)
                {
                    cells.Add(GetCellValue(cell));
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

    private static string GetCellValue(SSModel.ICell cell)
    {
        if (cell is null) return string.Empty;
        return cell.CellType switch
        {
            SSModel.CellType.String => cell.StringCellValue.Trim(),
            SSModel.CellType.Numeric => SSModel.DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? string.Empty
                : cell.NumericCellValue.ToString(),
            SSModel.CellType.Boolean => cell.BooleanCellValue.ToString(),
            SSModel.CellType.Formula => cell!.ToString().Trim(),
            _ => string.Empty,
        };
    }
}
