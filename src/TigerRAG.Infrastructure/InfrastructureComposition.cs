using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Qdrant.Client;
using StackExchange.Redis;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;

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
        services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();
        services.AddScoped<IDocumentAccessDal, DocumentAccessDal>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserRoleService>();
        services.AddScoped<DocumentAccessService>();

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
            configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.")));
        services.AddSingleton(_ => new QdrantClient(new Uri(
            configuration["Services:Qdrant"]
                ?? throw new InvalidOperationException("Services:Qdrant is required."))));
        // MinIO 客户端按 Endpoint Scheme 自动决定是否启用 HTTPS，便于本地 docker-compose 直连。
        services.AddSingleton<IMinioClient>(_ => CreateMinioClient(configuration));

        return services;
    }

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
