using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using TigerRAG.Application.Documents.Indexing;

namespace TigerRAG.Infrastructure.ObjectStorage;

/// <summary>MinIO 对象存储实现。Bucket 通过 <see cref="ObjectStorageOptions"/> 注入；路径形如 kb/{kbId}/{guid}/{fileName}。</summary>
public sealed class MinioFileStorage : IDocumentFileStorage
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly ILogger<MinioFileStorage> _logger;

    public MinioFileStorage(
        IMinioClient client,
        IOptions<ObjectStorageOptions> options,
        ILogger<MinioFileStorage> logger)
    {
        _client = client;
        _bucket = options.Value.Bucket
            ?? throw new InvalidOperationException($"{ObjectStorageOptions.DefaultSectionName}:Bucket is required.");
        _logger = logger;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // MinIO 回调流仅在回调期间有效，需在回调内复制到 MemoryStream 以确保调用方可读。
        var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(path)
            .WithCallbackStream((stream, cancellationToken) =>
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                tcs.TrySetResult(ms);
                return Task.CompletedTask;
            });
        _ = _client.GetObjectAsync(args, cancellationToken);
        return tcs.Task;
    }

    /// <summary>写入对象存储；失败记录错误日志后抛出，由调用方决定是否回滚。</summary>
    public async Task WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path)
                .WithStreamData(content)
                .WithObjectSize(content.Length)
                .WithContentType(contentType));
        }
        catch (Exception ex) when (ex is MinioException or IOException or OperationCanceledException)
        {
            _logger.LogError(ex, "对象存储写入失败。Bucket={Bucket} Path={Path}", _bucket, path);
            throw;
        }
    }

    /// <summary>删除对象存储中的文件；对象不存在视为成功；其他失败记录错误日志后抛出。</summary>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path));
        }
        catch (ObjectNotFoundException)
        {
            // 对象不存在：幂等成功，无需日志。
        }
        catch (Exception ex) when (ex is MinioException or IOException or OperationCanceledException)
        {
            _logger.LogError(ex, "对象存储删除失败。Bucket={Bucket} Path={Path}", _bucket, path);
            throw;
        }
    }
}
