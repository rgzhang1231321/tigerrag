namespace TigerRAG.Application.Auth;

/// <summary>首次部署管理员引导端口，由 <c>--bootstrap-admin</c> 命令调用，常规启动不触发。</summary>
public interface IAdminBootstrapper
{
    Task BootstrapAsync(string userName, string password, CancellationToken cancellationToken);
}