using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(UserRoleService userRoles) : ControllerBase
{
    /// <summary>
    /// 获取当前登录用户的标识、用户名和角色信息。
    /// </summary>
    /// <returns>当前用户信息；身份声明无效时返回非零业务码。</returns>
    [HttpGet("me")]
    public ActionResult<ApiResponse<UserResponse>> GetCurrent()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return Ok(ApiResponse.Success(new UserResponse(userId, User.Identity?.Name ?? string.Empty, roles)));
    }

    /// <summary>
    /// 获取系统中的用户列表，仅允许具有用户管理权限的用户访问。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>用户及其角色列表。</returns>
    [HttpGet]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserResponse>>>> List(CancellationToken cancellationToken)
    {
        var users = await userRoles.ListAsync(cancellationToken);
        return Ok(ApiResponse.Success(users.Select(UserResponse.From).ToArray()));
    }

    /// <summary>
    /// 创建用户并为其分配一个或多个系统固定角色。
    /// </summary>
    /// <param name="request">新用户的用户名、初始密码和角色。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>创建成功时返回 201 和用户信息；输入不合法时返回 400。</returns>
    [HttpPost]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRoles.CreateAsync(
                request.UserName,
                request.Password,
                request.Roles,
                cancellationToken);
            return Ok(ApiResponse.Success(UserResponse.From(user), "用户创建成功"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Validation, error.Message));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Validation, error.Message));
        }
    }

    /// <summary>
    /// 获取系统支持的固定角色列表。
    /// </summary>
    /// <returns>按名称排序的角色列表。</returns>
    [HttpGet("roles")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public ActionResult<ApiResponse<IReadOnlyCollection<string>>> ListRoles() =>
        Ok(ApiResponse.Success(SystemRoles.All.OrderBy(role => role, StringComparer.Ordinal).ToArray()));

    /// <summary>
    /// 使用请求中的角色集合替换指定用户的现有角色。
    /// </summary>
    /// <param name="userId">需要修改角色的用户标识。</param>
    /// <param name="request">需要分配的系统固定角色集合。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>更新成功时返回空数据；用户不存在时返回非零业务码。</returns>
    [HttpPut("{userId:guid}/roles")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<IActionResult> AssignRoles(
        Guid userId,
        AssignRolesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await userRoles.AssignRolesAsync(userId, request.Roles, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Validation, error.Message));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.NotFound, "用户不存在"));
        }
    }

    /// <summary>
    /// 管理员为指定用户设置临时密码，并撤销该用户的全部刷新会话。
    /// </summary>
    /// <param name="userId">需要重置密码的用户标识。</param>
    /// <param name="request">符合安全要求的新密码。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>重置成功时返回空数据；用户不存在时返回非零业务码。</returns>
    [HttpPut("{userId:guid}/password")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await userRoles.ResetPasswordAsync(userId, request.NewPassword, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.NotFound, "用户不存在"));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Validation, error.Message));
        }
    }
}

public sealed record UserResponse(Guid Id, string UserName, IReadOnlyList<string> Roles)
{
    public static UserResponse From(UserAccount user) => new(user.Id, user.UserName, user.Roles);
}

public sealed record AssignRolesRequest([Required] IReadOnlyCollection<string> Roles);

public sealed record CreateUserRequest(
    [Required] string UserName,
    [Required, MinLength(10)] string Password,
    [Required] IReadOnlyCollection<string> Roles);

public sealed record ResetPasswordRequest([Required, MinLength(10)] string NewPassword);
