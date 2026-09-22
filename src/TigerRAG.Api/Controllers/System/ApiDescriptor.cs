namespace TigerRAG.Api.Controllers.System;

/// <summary>API 服务描述：服务名称 + 当前接口版本。</summary>
public sealed record ApiDescriptor(string Name, string Version);