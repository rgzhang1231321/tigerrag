namespace TigerRAG.Application.Auth;

/// <summary>声明某个 action 属于哪个菜单下的哪个 endpoint，供 [MenuEndpointAuthFilter] 在请求时校验授权。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MenuEndpointAttribute(
    string menuKey, string endpointKey, string description) : Attribute
{
    public string MenuKey { get; } = menuKey;
    public string EndpointKey { get; } = endpointKey;
    public string Description { get; } = description;
}