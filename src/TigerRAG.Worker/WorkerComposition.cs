using TigerRAG.Infrastructure;

namespace TigerRAG.Worker;

/// <summary>Worker 组合根；使用 Worker 专属基础设施（不含 Identity/认证/授权）并启动 <see cref="DocumentIndexingWorker"/>。</summary>
public static class WorkerComposition
{
    public static IServiceCollection AddTigerRagWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddWorkerInfrastructure(configuration);
        services.AddHostedService<DocumentIndexingWorker>();
        return services;
    }
}
