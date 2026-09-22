namespace TigerRAG.Application.Auth;

/// <summary>扫描后的 endpoint 描述；用于授权 filter 与角色授权 UI 展示。</summary>
public sealed record MenuEndpointDescriptor(
    string MenuKey,
    string EndpointKey,
    string Description,
    string HttpMethod,
    string Path);