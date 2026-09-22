using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Filters;

/// <summary>全局 endpoint 授权 filter：所有业务 action 必须挂 <c>[MenuEndpoint]</c>，授权事实源为 <c>role_endpoint_grant</c> 表。无 <c>[MenuEndpoint]</c> 的 action（如 [AllowAnonymous] 登录/刷新）放行。所有角色（含 Admin）一视同仁，Admin 默认全通由启动期 bootstrap 灌全量 grant 保证。</summary>
public sealed class MenuEndpointAuthFilter(
    IMenuEndpointRegistry registry,
    IRoleEndpointGrantStore store,
    ILogger<MenuEndpointAuthFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var descriptor = context.ActionDescriptor.EndpointMetadata
            .OfType<MenuEndpointAttribute>()
            .Select(attr => registry.Find(attr.EndpointKey))
            .FirstOrDefault();
        if (descriptor is null) return;                         // 无 [MenuEndpoint] 的 action（如 [AllowAnonymous] 登录/刷新）放行

        var userRoles = context.HttpContext.User
            .FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        if (await store.HasGrantAsync(userRoles, descriptor.EndpointKey, context.HttpContext.RequestAborted))
            return;

        logger.LogInformation("endpoint {Key} 拒绝：角色 {Roles} 未授权",
            descriptor.EndpointKey, string.Join(',', userRoles));

        // 直接返回 ApiResponse 外壳，避免 ApiResponseFilter 二次包装
        context.Result = new ObjectResult(
            ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, $"无权限访问 {descriptor.EndpointKey}"))
        {
            StatusCode = StatusCodes.Status200OK
        };
    }
}
