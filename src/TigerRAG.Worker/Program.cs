using TigerRAG.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTigerRagWorker(builder.Configuration);

var host = builder.Build();
host.Run();
