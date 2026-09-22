namespace TigerRAG.Infrastructure.ObjectStorage;

/// <summary>对象存储 Bucket 选项；与具体实现（MinIO/S3/OSS）解耦。</summary>
public sealed class ObjectStorageOptions
{
    /// <summary>配置节名。</summary>
    public const string DefaultSectionName = "Services:Minio";

    /// <summary>默认 Bucket 名称；实现可在构造时按需读取。</summary>
    public string? Bucket { get; set; }
}
