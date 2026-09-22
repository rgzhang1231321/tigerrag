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

    public MinioFileStorage(IMinioClient client, IOptions<ObjectStorageOptions> options)
    {
        _client = client;
        _bucket = options.Value.Bucket
            ?? throw new InvalidOperationException($"{ObjectStorageOptions.DefaultSectionName}:Bucket is required.");
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // MinIO 7.0 用回调流替代了直接返回；用 TaskCompletionSource 桥接到 Task<Stream>。
        var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(path)
            .WithCallbackStream((stream, cancellationToken) =>
            {
                tcs.TrySetResult(stream);
                return Task.CompletedTask;
            });
        // 同步启动，调用方 await tcs.Task 拿到流；MinIO 回调内会填充。
        _ = _client.GetObjectAsync(args, cancellationToken);
        return tcs.Task;
    }

    public async Task<bool> WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
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
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path));
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }
}
