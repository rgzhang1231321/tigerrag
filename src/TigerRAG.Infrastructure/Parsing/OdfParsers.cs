using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>ODT 解析器：ZIP 解压后提取 content.xml 中的 &lt;text:p&gt; 节点。</summary>
internal sealed class OdtParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.oasis.opendocument.text",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtractTextFromOdf(content, "text:p", cancellationToken));
    }

    /// <summary>从 ODF content.xml 中提取指定本地名称的元素文本。</summary>
    internal static string ExtractTextFromOdf(Stream content, string elementName, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("content.xml");
        if (entry is null)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        using var entryStream = entry.Open();
        var doc = XDocument.Load(entryStream);

        var targetLocalName = elementName.Split(':').LastOrDefault() ?? elementName;
        foreach (var element in doc.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Name.LocalName.Equals(targetLocalName, StringComparison.OrdinalIgnoreCase))
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    sb.AppendLine(text);
                }
            }
        }

        return sb.ToString().Trim();
    }
}

/// <summary>ODS 解析器：ZIP 解压后提取 content.xml 中的表格行单元格文本。</summary>
internal sealed class OdsParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.oasis.opendocument.spreadsheet",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("content.xml");
        if (entry is null)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new System.Text.StringBuilder();
        using var entryStream = entry.Open();
        using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
        });

        var currentRowCells = new List<string>();
        var inRow = false;
        var cellDepth = 0;
        var rowDepth = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = reader.Name;

            if (reader.NodeType == XmlNodeType.Element && name.EndsWith("table-row", StringComparison.OrdinalIgnoreCase))
            {
                inRow = true;
                rowDepth = reader.Depth;
                currentRowCells.Clear();
            }
            else if (inRow && reader.NodeType == XmlNodeType.Element && name.EndsWith("table-cell", StringComparison.OrdinalIgnoreCase))
            {
                cellDepth = reader.Depth;
                if (!reader.IsEmptyElement)
                {
                    currentRowCells.Add(ReadCellText(reader));
                }
                else
                {
                    currentRowCells.Add(string.Empty);
                }
            }
            else if (inRow && reader.NodeType == XmlNodeType.EndElement && name.EndsWith("table-row", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRowCells.Count > 0)
                {
                    sb.AppendLine(string.Join("\t", currentRowCells));
                }
                inRow = false;
            }
        }

        return Task.FromResult(sb.ToString().Trim());
    }

    /// <summary>读取单元格内的纯文本。</summary>
    private static string ReadCellText(XmlReader reader)
    {
        var sb = new System.Text.StringBuilder();
        var startDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth)
            {
                break;
            }
            if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
            {
                sb.Append(reader.Value);
            }
        }
        return sb.ToString().Trim();
    }
}

/// <summary>ODP 解析器：ZIP 解压后提取 content.xml 中的文本框内容。</summary>
internal sealed class OdpParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.oasis.opendocument.presentation",
    };

    public IReadOnlySet<string> MimeTypes => Mimes;

    public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("content.xml");
        if (entry is null)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new System.Text.StringBuilder();
        using var entryStream = entry.Open();
        using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
        });

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.Name.EndsWith("text-box", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.IsEmptyElement)
                {
                    var boxXml = reader.ReadInnerXml();
                    var texts = ExtractTextP(boxXml);
                    if (texts.Count > 0)
                    {
                        sb.AppendLine(string.Join("\n", texts));
                        sb.AppendLine("---");
                    }
                }
            }
        }

        return Task.FromResult(sb.ToString().Trim());
    }

    private static List<string> ExtractTextP(string boxXml)
    {
        var texts = new List<string>();
        using var reader = XmlReader.Create(new System.IO.StringReader(boxXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            ConformanceLevel = ConformanceLevel.Fragment,
        });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name.EndsWith("text:p", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.IsEmptyElement)
                {
                    texts.Add(reader.ReadInnerXml().Trim());
                }
            }
        }
        return texts;
    }
}
