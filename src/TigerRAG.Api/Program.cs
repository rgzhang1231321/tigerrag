using TigerRAG.Api;
using TigerRAG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTigerRagInfrastructure(builder.Configuration);

if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    var bootstrapApp = builder.Build();
    await AdminBootstrapCommand.TryRunAsync(
        bootstrapApp.Services,
        bootstrapApp.Configuration,
        args,
        CancellationToken.None);
    return;
}

builder.Services.AddTigerRagApi(builder.Configuration);
var app = builder.Build();
app.MapTigerRagApi();
app.Run();

public partial class Program;
