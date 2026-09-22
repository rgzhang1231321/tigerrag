namespace TigerRAG.Application.Auth;

/// <summary>单个 endpoint 的授权视图。</summary>
public sealed record EndpointGrantView(
    string EndpointKey,
    string Description,
    string HttpMethod,
    string Path,
    bool Granted);