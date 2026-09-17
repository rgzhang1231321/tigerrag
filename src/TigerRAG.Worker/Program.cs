using TigerRAG.Worker;
using TigerRAG.Infrastructure.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddLocalDotEnv(builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);
builder.Services.AddTigerRagWorker(builder.Configuration);

var host = builder.Build();
host.Run();
