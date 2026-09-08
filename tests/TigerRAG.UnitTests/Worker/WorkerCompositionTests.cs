using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minio;
using Qdrant.Client;
using StackExchange.Redis;
using TigerRAG.Worker;

namespace TigerRAG.UnitTests.Worker;

public sealed class WorkerCompositionTests
{
    [Fact]
    public void AddTigerRagWorker_RegistersDocumentIndexingHostedService()
    {
        var services = new ServiceCollection();

        services.AddTigerRagWorker(CreateConfiguration());

        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Equal(typeof(DocumentIndexingWorker), registration.ImplementationType);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(QdrantClient));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMinioClient));
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=ragdb;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
            ["Services:Qdrant"] = "http://localhost:6334",
            ["Services:Minio:Endpoint"] = "http://localhost:9000",
            ["Services:Minio:AccessKey"] = "test",
            ["Services:Minio:SecretKey"] = "test-secret"
        })
        .Build();
}
