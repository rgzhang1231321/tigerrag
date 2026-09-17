using TigerRAG.Infrastructure;

namespace TigerRAG.Worker;

/// <summary>Worker 组合根；复用 Infrastructure 注册并启动 <see cref="DocumentIndexingWorker"/>。</summary>
public static class WorkerComposition
{
    public static IServiceCollection AddTigerRagWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTigerRagInfrastructure(configuration);
        services.AddHostedService<DocumentIndexingWorker>();
        return services;
    }
}
