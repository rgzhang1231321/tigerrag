using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Qdrant.Client;
using StackExchange.Redis;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.Menus;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Statistics;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.ApiLogs.Dal;
using TigerRAG.Infrastructure.Auth;
using TigerRAG.Infrastructure.Auth.Dal;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Documents.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Menus.Dal;
using TigerRAG.Infrastructure.Indexing;
using TigerRAG.Infrastructure.ObjectStorage;
using TigerRAG.Infrastructure.OperationAudit.Dal;
using TigerRAG.Infrastructure.Parsing;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Queue;
using TigerRAG.Infrastructure.Roles.Dal;
using TigerRAG.Infrastructure.Statistics.Dal;
using TigerRAG.Infrastructure.Users;
using TigerRAG.Infrastructure.KnowledgeBases.Dal;

namespace TigerRAG.Infrastructure;

/// <summary>
/// Infrastructure 组合根。注册 DbContext、Identity、外部 SDK 客户端以及 Application 端口实现。
/// Api 与 Worker 通过本扩展统一装配基础设施。
/// </summary>
public static class InfrastructureComposition
{
    public static IServiceCollection AddTigerRagInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<TigerRagDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgreSql")));
        services
            .AddIdentityCore<AppUser>(options =>
            {
                // 登录失败 5 次锁定 15 分钟；密码最小长度 10。
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 5;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<TigerRagDbContext>();

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<RefreshTokenOptions>(configuration.GetSection("RefreshToken"));
        services.AddScoped<IUserDal, UserDal>();
        services.AddScoped<IUserCredentialDal, UserDal>();
        services.AddScoped<IRefreshSessionDal, RefreshSessionDal>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();
        services.AddScoped<IUserSecurityStampRotator, UserSecurityStampRotator>();
        services.AddScoped<IDocumentAccessDal, DocumentAccessDal>();
        services.AddScoped<IDocumentQueryDal, DocumentQueryDal>();
        services.AddScoped<IDocumentLifecycleDal, DocumentLifecycleDal>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentIndexingService>();
        services.AddScoped<IMenuConfigDal, MenuConfigDal>();
        services.AddScoped<IRoleMenuReference, MenuReferenceDal>();
        services.AddScoped<IRoleAdmin, RoleAdminDal>();
        services.AddScoped<IRoleRegistry, RoleRegistry>();
        services.AddScoped<RoleAdminService>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserRoleService>();
        services.AddScoped<DocumentAccessService>();
        services.AddScoped<IStatisticsDal, StatisticsDal>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IApiLogDal, ApiLogDal>();
        services.AddScoped<ApiLogService>();
        services.AddScoped<IOperationAuditDal, OperationAuditDal>();
        services.AddScoped<IOperationAuditWriter, OperationAuditDal>();
        services.AddScoped<IKbDal, KbDal>();
        services.AddScoped<IUserLookup, UserLookup>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<IKbAccessDal, KbAccessDal>();
        services.AddScoped<KnowledgeBaseAccessService>();
        // 角色-Endpoint 授权缓存 L1：键 auth:role:{role}:endpoints（Redis Set），TTL 默认 5 分钟，由装饰器在写路径失效。
        services.AddScoped<RoleEndpointGrantStore>();
        services.AddScoped<IRoleEndpointGrantStore>(sp => new CachedRoleEndpointGrantStore(
            sp.GetRequiredService<RoleEndpointGrantStore>(),
            sp.GetRequiredService<IRoleEndpointGrantCache>(),
            sp.GetRequiredService<ILogger<CachedRoleEndpointGrantStore>>()));
        services.AddScoped<RoleEndpointGrantService>();

        // Redis 连接：显式配置避免 3.x 默认 backlog 超时过短导致间歇性 AuthenticationFailure。
        // AbortOnConnectFail=false 让连接断开后持续重连；默认 true 会让重连时 AUTH 失败后直接放弃。
        // ConnectRetry=5 + ConnectTimeout=10s 覆盖网络抖动时的自动重认证。
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connStr = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
            var config = ConfigurationOptions.Parse(connStr);
            config.AbortOnConnectFail = false;
            config.ConnectTimeout = 10000;
            config.SyncTimeout = 10000;
            config.ConnectRetry = 5;
            config.Ssl = false;
            config.AllowAdmin = true;
            return ConnectionMultiplexer.Connect(config);
        });
        // 文档索引任务队列：Redis List（FIFO），键 doc:index:queue。
        services.AddSingleton<IDocumentIndexQueue, RedisDocumentIndexQueue>();
        // JWT 按用户撤权的 Redis L1：键 auth:user:{guid}:stamp，TTL = AccessTokenMinutes*60 + ClockSkewSeconds。
        services.AddSingleton<IAuthRevocationCache, RedisAuthRevocationCache>();
        services.AddSingleton<IRoleEndpointGrantCache, RedisRoleEndpointGrantCache>();
        services.Configure<GrantCacheOptions>(configuration.GetSection(GrantCacheOptions.DefaultSectionName));
        services.AddSingleton<IMenuEndpointRegistry>(sp =>
        {
            var provider = sp.GetRequiredService<IActionDescriptorCollectionProvider>();
            return new MenuEndpointRegistry(provider.ActionDescriptors.Items);
        });
        services.AddSingleton(_ => new QdrantClient(new Uri(
            configuration["Services:Qdrant:Url"]
                ?? throw new InvalidOperationException("Services:Qdrant is required."))));
        services.Configure<VectorIndexOptions>(configuration.GetSection(VectorIndexOptions.DefaultSectionName));
        services.AddSingleton<IVectorIndex, QdrantVectorIndex>();
        // MinIO 客户端按 Endpoint Scheme 自动决定是否启用 HTTPS，便于本地 docker-compose 直连。
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
    /// <param name="configuration">配置根。</param>
    /// <returns>已构建的 MinIO 客户端实例。</returns>
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
