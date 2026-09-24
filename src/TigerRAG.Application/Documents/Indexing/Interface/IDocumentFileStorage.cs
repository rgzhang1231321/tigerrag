namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>原始文件读写端口，由对象存储实现。</summary>
public interface IDocumentFileStorage
{
    /// <summary>读取对象存储中的文件流。</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>写入对象存储；失败抛异常。</summary>
    Task WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>删除对象存储中的文件；对象不存在视为成功；失败抛异常。</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
