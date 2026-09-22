namespace TigerRAG.Api.Controllers.Roles;

/// <summary>单 endpoint 切换请求。</summary>
public sealed record ToggleEndpointRequest(
    string EndpointKey,
    string MenuKey,
    bool Grant);