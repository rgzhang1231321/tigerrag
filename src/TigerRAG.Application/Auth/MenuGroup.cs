namespace TigerRAG.Application.Auth;

/// <summary>单个菜单下的 endpoint 授权视图。</summary>
public sealed record MenuGroup(string MenuKey, IReadOnlyList<EndpointGrantView> Endpoints);