using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Users;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>用户管理端点；通过 <c>[MenuEndpoint]</c> 统一授权，Admin 由全局 filter bypass。</summary>
[ApiController]
[Route("api/users")]
public sealed class UsersController(
    UserRoleService userRoles,
    IRoleAdmin roleAdmin) : ControllerBase
{
    /// <summary>
    /// 获取当前登录用户的标识、用户名和角色信息。
    /// </summary>
    /// <returns>当前用户信息；身份声明无效时返回非零业务码。</returns>
    [HttpPost("me")]
    [MenuEndpoint("users", "users.me", "获取当前登录用户信息")]
    public ActionResult<ApiResponse<UserResponse>> GetCurrent()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return Ok(ApiResponse.Success(new UserResponse(userId, User.Identity?.Name ?? string.Empty, roles, false)));
    }

    /// <summary>
    /// 获取系统中的用户列表。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>用户及其角色列表。</returns>
    [HttpPost("list")]
    [MenuEndpoint("users", "users.list", "查询用户列表")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserResponse>>>> List(CancellationToken cancellationToken)
    {
        var users = await userRoles.ListAsync(cancellationToken);
        return Ok(ApiResponse.Success(users.Select(user => UserResponse.From(user)).ToArray()));
    }

    /// <summary>
    /// 创建用户并为其分配一个或多个系统固定角色（不设置密码）。
    /// 客户端拿到响应中的用户 Id 后，再调用 <c>POST /api/users/{id}/initial-password</c> 设置密码。
    /// </summary>
    /// <param name="request">新用户的用户名与角色集合。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>创建成功时返回 201 与新用户信息；输入不合法时返回 400。</returns>
    [HttpPost]
    [MenuEndpoint("users", "users.create", "创建用户")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRoles.CreateAsync(request.UserName, request.Roles, cancellationToken);
            return Ok(ApiResponse.Success(UserResponse.From(user), "用户创建成功"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>
    /// 为新创建的用户设置初始密码（<c>MD5(password+salt)</c>，salt 由后端在创建时生成）。
    /// </summary>
    /// <param name="userId">新用户的标识，由创建用户接口返回。</param>
    /// <param name="request">客户端按 <c>MD5(password+salt)</c> 计算后的密码哈希。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>设置成功时返回空数据；用户不存在时返回非零业务码。</returns>
    [HttpPost("{userId:guid}/initial-password")]
    [MenuEndpoint("users", "users.initialPassword", "设置初始密码")]
    public async Task<IActionResult> SetInitialPassword(
        Guid userId,
        SetInitialPasswordRequest request,
        CancellationToken cancellationToken)
    {
        // 入口处做格式校验：客户端提交的必须是 32 位小写 hex。
        if (!TryValidatePasswordHash(request.PasswordHash, out var formatError))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, formatError));
        }

        try
        {
            await userRoles.SetInitialPasswordAsync(userId, request.PasswordHash, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>
    /// 获取系统中的角色列表（按字母序，仅返回角色名字符串）。
    /// </summary>
    /// <returns>角色名字符串集合。</returns>
    [HttpPost("roles/list")]
    [MenuEndpoint("users", "users.roles.list", "查询角色列表")]
    [Obsolete("请改用 POST /api/roles/list，保留此端点仅作过渡兼容。")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<string>>>> ListRoles(CancellationToken cancellationToken)
    {
        var roles = await roleAdmin.ListAsync(cancellationToken);
        return Ok(ApiResponse.Success<IReadOnlyCollection<string>>(roles.Select(role => role.Name).ToArray()));
    }

    /// <summary>
    /// 使用请求中的角色集合替换指定用户的现有角色。
    /// </summary>
    /// <param name="userId">需要修改角色的用户标识。</param>
    /// <param name="request">需要分配的角色集合。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>更新成功时返回空数据；用户不存在时返回非零业务码；自我降级为最后一名 Admin 时返回校验失败。</returns>
    [HttpPost("{userId:guid}/roles")]
    [MenuEndpoint("users", "users.assignRoles", "分配用户角色")]
    public async Task<IActionResult> AssignRoles(
        Guid userId,
        AssignRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            await userRoles.AssignRolesAsync(actorId, userId, request.Roles, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
        }
    }

    /// <summary>
    /// 管理员为指定用户设置新密码（<c>MD5(password+salt)</c>），并撤销该用户的全部刷新会话。
    /// salt 复用 DB 中既有值，管理员 UI 需先调用 <c>POST /api/auth/salt</c> body <c>{userName}</c> 取 salt。
    /// </summary>
    /// <param name="userId">需要重置密码的用户标识。</param>
    /// <param name="request">新密码的客户端哈希。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>重置成功时返回空数据；用户不存在时返回非零业务码。</returns>
    [HttpPost("{userId:guid}/password")]
    [MenuEndpoint("users", "users.resetPassword", "重置用户密码")]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        // 入口处做格式校验：客户端提交的必须是 32 位小写 hex。
        if (!TryValidatePasswordHash(request.PasswordHash, out var formatError))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, formatError));
        }

        try
        {
            await userRoles.ResetPasswordAsync(userId, request.PasswordHash, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>
    /// 删除指定用户。先撤销该用户全部刷新会话，再删账号。用户不存在时返回 404。
    /// </summary>
    [HttpPost("{userId:guid}/delete")]
    [MenuEndpoint("users", "users.delete", "删除用户")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        // 自保护：管理员不能删除自己，避免系统陷入无管理员状态。
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) && actorId == userId)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, "不能删除当前登录的自身账号"));
        }

        var deleted = await userRoles.DeleteAsync(userId, cancellationToken);
        return deleted
            ? Ok(ApiResponse<object?>.Success(null))
            : Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
    }

    /// <summary>
    /// 设置用户锁口：body.locked=true 时锁定并强制撤销全部会话；false 时解锁。用户不存在时返回 404。
    /// </summary>
    [HttpPost("{userId:guid}/lock")]
    [MenuEndpoint("users", "users.lock", "锁定或解锁用户")]
    public async Task<IActionResult> SetLockout(
        Guid userId,
        SetLockoutRequest request,
        CancellationToken cancellationToken)
    {
        // 自保护：管理员不能锁定自己。
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) && actorId == userId)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, "不能锁定当前登录的自身账号"));
        }

        try
        {
            var lockoutEnd = request.Locked ? DateTimeOffset.UtcNow.AddYears(100) : (DateTimeOffset?)null;
            await userRoles.SetLockoutAsync(userId, lockoutEnd, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
        }
    }

    private static bool TryValidatePasswordHash(string passwordHash, out string error) =>
        PasswordHashValidator.TryValidate(passwordHash, out error);
}

public sealed record UserResponse(Guid Id, string UserName, IReadOnlyList<string> Roles, bool IsLocked)
{
    public static UserResponse From(UserListItem user) => new(user.Id, user.UserName, user.Roles, user.IsLocked);
    public static UserResponse From(UserAccount user) => new(user.Id, user.UserName, user.Roles, false);
}

public sealed record AssignRolesRequest([Required] IReadOnlyCollection<string> Roles);

public sealed record CreateUserRequest(
    [Required] string UserName,
    [Required] IReadOnlyCollection<string> Roles);

public sealed record SetInitialPasswordRequest([Required] string PasswordHash);

public sealed record ResetPasswordRequest([Required] string PasswordHash);

public sealed record SetLockoutRequest([Required] bool Locked);