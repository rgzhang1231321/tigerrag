using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Qdrant.Client;
using StackExchange.Redis;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure;
using TigerRAG.Infrastructure.Auth;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Documents.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Auth;
using TigerRAG.Infrastructure.Persistence.Entities.Conversations;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;
using TigerRAG.Infrastructure.Persistence.Entities.Menus;
using TigerRAG.Infrastructure.Persistence.Entities.OperationAudit;
using TigerRAG.Infrastructure.Users;

namespace TigerRAG.IntegrationTests.Infrastructure;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddTigerRagInfrastructure_RegistersRequiredSdkAndDalServices()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddTigerRagInfrastructure(configuration);

        Assert.Contains(services, item => item.ServiceType == typeof(DbContextOptions<TigerRagDbContext>));
        Assert.Contains(services, item => item.ServiceType == typeof(IUserDal) && item.ImplementationType == typeof(UserDal));
        Assert.Contains(services, item => item.ServiceType == typeof(IDocumentAccessDal) && item.ImplementationType == typeof(DocumentAccessDal));
        Assert.Contains(services, item => item.ServiceType == typeof(IAccessTokenIssuer));
        Assert.Contains(services, item => item.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, item => item.ServiceType == typeof(QdrantClient));
        Assert.Contains(services, item => item.ServiceType == typeof(IMinioClient));
    }

    [Fact]
    public void TigerRagDbContext_ContainsCoreRbacAndRagTables()
    {
        var options = new DbContextOptionsBuilder<TigerRagDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test;Username=test;Password=test")
            .Options;
        using var context = new TigerRagDbContext(options);

        var entityTypes = context.Model.GetEntityTypes().Select(type => type.ClrType).ToArray();

        Assert.Contains(typeof(AppUser), entityTypes);
        Assert.Contains(typeof(knowledge_base_record), entityTypes);
        Assert.Contains(typeof(document_record), entityTypes);
        Assert.Contains(typeof(document_permission_record), entityTypes);
        Assert.Contains(typeof(conversation_record), entityTypes);
        Assert.Contains(typeof(message_record), entityTypes);
        Assert.Contains(typeof(audit_log_record), entityTypes);
        Assert.Contains(typeof(refresh_token_record), entityTypes);
        Assert.Contains(typeof(menu_config_record), entityTypes);
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=ragdb;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
            ["Services:Qdrant"] = "http://localhost:6334",
            ["Services:Minio:Endpoint"] = "http://localhost:9000",
            ["Services:Minio:AccessKey"] = "test",
            ["Services:Minio:SecretKey"] = "test-secret",
            ["Jwt:Issuer"] = "TigerRAG",
            ["Jwt:Audience"] = "TigerRAG",
            ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters"
        })
        .Build();
}
