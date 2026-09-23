using System.Reflection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

using TigerRAG.Application.Auth;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>启动时一次性扫描所有 action 上的 [MenuEndpoint]，构造不可变查找表。</summary>
public sealed class MenuEndpointRegistry : IMenuEndpointRegistry
{
    private readonly Dictionary<string, MenuEndpointDescriptor> _byKey;

    public IReadOnlyList<MenuEndpointDescriptor> All { get; }

    public MenuEndpointRegistry(IEnumerable<ActionDescriptor> actionDescriptors)
    {
        var items = new List<MenuEndpointDescriptor>();
        foreach (var action in actionDescriptors)
        {
            var methodInfo = (action as ControllerActionDescriptor)?.MethodInfo;
            if (methodInfo is null) continue;

            foreach (var attribute in action.EndpointMetadata.OfType<MenuEndpointAttribute>())
            {
                // 强制 description 必填：空描述会让角色授权 UI 变成无意义的勾选，扫描阶段直接抛错。
                if (string.IsNullOrWhiteSpace(attribute.Description))
                {
                    throw new InvalidOperationException(
                        $"[MenuEndpoint] on {methodInfo.DeclaringType?.Name}.{methodInfo.Name} " +
                        $"(endpointKey={attribute.EndpointKey}) 必须填写 description。");
                }

                var (httpMethod, path) = ExtractHttp(methodInfo);
                items.Add(new MenuEndpointDescriptor(
                    attribute.MenuKey,
                    attribute.EndpointKey,
                    attribute.Description,
                    httpMethod,
                    path));
            }
        }

        _byKey = items.ToDictionary(item => item.EndpointKey, StringComparer.Ordinal);
        All = items;
    }

    public MenuEndpointDescriptor? Find(string endpointKey) =>
        _byKey.GetValueOrDefault(endpointKey);

    private static (string HttpMethod, string Path) ExtractHttp(MethodInfo method)
    {
        var httpAttribute = method.GetCustomAttributes<HttpMethodAttribute>(inherit: false).FirstOrDefault();
        var httpMethod = httpAttribute?.HttpMethods.FirstOrDefault() ?? "ANY";
        var path = httpAttribute?.Template
            ?? method.GetCustomAttribute<RouteAttribute>(inherit: false)?.Template
            ?? string.Empty;
        return (httpMethod, path);
    }
}