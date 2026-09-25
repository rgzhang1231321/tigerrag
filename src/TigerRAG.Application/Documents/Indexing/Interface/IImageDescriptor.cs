namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>图片语义描述端口；将图片字节转换为自然语言描述。实现使用 LLM 多模态视觉模型。</summary>
public interface IImageDescriptor
{
    /// <summary>生成图片的自然语言描述。</summary>
    /// <param name="imageBytes">图片原始字节。</param>
    /// <param name="mimeType">图片 MIME 类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>图片描述文本。</returns>
    Task<string> DescribeAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        CancellationToken cancellationToken);
}
