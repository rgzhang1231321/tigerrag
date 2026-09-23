using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Qdrant.Client;
using StackExchange.Redis;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Documents.Dal;
using TigerRAG.Infrastructure.Indexing;
using TigerRAG.Infrastructure.ObjectStorage;
using TigerRAG.Infrastructure.OperationAudit.Dal;
using TigerRAG.Infrastructure.Parsing;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Queue;

namespace TigerRAG.Infrastructure;

/// <summary>Worker 专用基础设施组合：仅注册索引编排所需的最小服务集，不含 Identity/认证/授权。</summary>
public static class WorkerInfrastructureComposition
{
    /// <summary>注册 Worker 专属基础设施：DbContext、文档 DAL、队列、向量索引、对象存储、审计。</summary>
    public static IServiceCollection AddWorkerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<TigerRagDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgreSql")));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IDocumentAccessDal, DocumentAccessDal>();
        services.AddScoped<IDocumentQueryDal, DocumentQueryDal>();
        services.AddScoped<IDocumentLifecycleDal, DocumentLifecycleDal>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<DocumentIndexingService>();
        services.AddScoped<IOperationAuditDal, OperationAuditDal>();
        services.AddScoped<IOperationAuditWriter, OperationAuditDal>();

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
            configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.")));
        services.AddSingleton<IDocumentIndexQueue, RedisDocumentIndexQueue>();

        services.AddSingleton(_ => new QdrantClient(new Uri(
            configuration["Services:Qdrant:Url"]
                ?? throw new InvalidOperationException("Services:Qdrant is required."))));
        services.Configure<VectorIndexOptions>(configuration.GetSection(VectorIndexOptions.DefaultSectionName));
        services.AddSingleton<IVectorIndex, QdrantVectorIndex>();

        services.AddSingleton<IMinioClient>(_ => CreateMinioClient(configuration));
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.DefaultSectionName));
        services.AddSingleton<IDocumentFileStorage, MinioFileStorage>();
        services.AddSingleton<IDocumentParser, MimeDispatchingParser>();
        services.AddSingleton<ITextChunker, FixedWindowChunker>();
        services.AddSingleton<IEmbeddingGenerator, HashEmbeddingGenerator>();
        services.Configure<DocumentIndexQueueOptions>(configuration.GetSection(DocumentIndexQueueOptions.DefaultSectionName));

        return services;
    }

    /// <summary>根据配置创建 MinIO 客户端。按 Endpoint Scheme 自动决定是否启用 HTTPS。</summary>
    private static IMinioClient CreateMinioClient(IConfiguration configuration)
    {
        var endpoint = new Uri(configuration["Services:Minio:Endpoint"]
            ?? throw new InvalidOperationException("Services:Minio:Endpoint is required."));
        var accessKey = configuration["Services:Minio:AccessKey"]
            ?? throw new InvalidOperationException("Services:Minio:AccessKey is required.");
        var secretKey = configuration["Services:Minio:SecretKey"]
            ?? throw new InvalidOperationException("Services:Minio:SecretKey is required.");

        return new MinioClient()
            .WithEndpoint(endpoint.Host, endpoint.Port)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
            .Build();
    }
}
