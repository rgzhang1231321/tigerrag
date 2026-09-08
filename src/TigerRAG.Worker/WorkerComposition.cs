using TigerRAG.Infrastructure;

namespace TigerRAG.Worker;

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
