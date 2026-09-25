using TigerRAG.Application.Documents.Indexing.Interface;

namespace TigerRAG.Infrastructure.Parsing;

/// <summary>图片解析器：通过 IImageDescriptor 生成图片的自然语言描述文本。</summary>
internal sealed class ImageParser : ISpecificParser
{
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/bmp",
        "image/tiff",
    };

    private readonly IImageDescriptor? _imageDescriptor;

    public ImageParser(IImageDescriptor? imageDescriptor)
    {
        _imageDescriptor = imageDescriptor;
    }

    public IReadOnlySet<string> MimeTypes => Mimes;

    public async Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_imageDescriptor is null)
        {
            return "[图片: 未配置描述服务]";
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var description = await _imageDescriptor.DescribeAsync(
                bytes, "image/png", cancellationToken);
            return description;
        }
        catch
        {
            return "[图片: 描述失败]";
        }
    }
}
