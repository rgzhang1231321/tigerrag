using Microsoft.Extensions.Options;
using Minio;
using Minio.Exceptions;
using TigerRAG.Application.Documents;

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

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(path)
            .WithCallbackStream(stream => stream));
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
