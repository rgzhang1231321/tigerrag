namespace TigerRAG.Application.Documents.Indexing;

/// <summary>从文档中提取的图片信息。当前阶段解析器不填充，后续图片提取阶段填充。</summary>
/// <param name="Bytes">图片原始字节数据。</param>
/// <param name="Position">图片在文档文本中的字符偏移量（-1 表示未知位置）。</param>
public sealed record ExtractedImage(
    ReadOnlyMemory<byte> Bytes,
    int Position);
