using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Api.Controllers.Users;

namespace TigerRAG.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    private const string RefreshCookieName = "tigerrag.refresh";

    /// <summary>
    /// 取用户的密码 salt（用于客户端计算 <c>MD5(password+salt)</c>）；用户不存在时返回 404。
    /// salt 不构成机密，仅用于抗离线暴力；返回 404 仅提示用户名是否存在，与登录失败时的统一错误分开。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("salt")]
    public async Task<ActionResult<ApiResponse<SaltResponse>>> GetSalt(
        [FromBody] SaltRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, "用户名为必填项"));
        }

        var salt = await authService.GetSaltAsync(request.UserName, cancellationToken);
        if (salt is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "用户不存在"));
        }

        return Ok(ApiResponse.Success(new SaltResponse(salt)));
    }

    /// <summary>
    /// 使用用户名与客户端 <c>MD5(password+salt)</c> 登录；返回访问令牌，并通过 HttpOnly Cookie 写入刷新令牌。
    /// 登录成功由 AuthService 内部记录审计。
    /// </summary>
    /// <param name="request">登录用户名与客户端计算好的密码哈希。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>登录成功时返回访问令牌和当前用户信息；凭据无效时返回非零业务码。</returns>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        // 入口处做格式校验：客户端提交的必须是 32 位小写 hex。
        if (!PasswordHashValidator.TryValidate(request.PasswordHash, out var hashError))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, hashError));
        }

        var result = await authService.LoginAsync(request.UserName, request.PasswordHash, cancellationToken);
        if (result is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户名或密码错误"));
        }

        SetRefreshCookie(result.RefreshToken);
        return Ok(ToResponse(result));
    }

    /// <summary>
    /// 使用刷新 Cookie 轮换刷新令牌，并签发新的访问令牌。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>刷新成功时返回新的访问令牌和用户信息；刷新令牌无效时返回非零业务码。</returns>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "缺少刷新令牌"));
        }

        var result = await authService.RefreshAsync(refreshToken, cancellationToken);
        if (result is null)
        {
            DeleteRefreshCookie();
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "刷新令牌无效或已过期"));
        }

        SetRefreshCookie(result.RefreshToken);
        return Ok(ToResponse(result));
    }

    /// <summary>
    /// 撤销当前刷新会话并删除浏览器中的刷新 Cookie。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>退出处理完成后返回 204。</returns>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken);
        }

        DeleteRefreshCookie();
        return Ok(ApiResponse<object?>.Success(null));
    }

    /// <summary>
    /// 修改当前登录用户的密码，并撤销该用户的全部刷新会话。
    /// 当前与新密码均为客户端按 <c>MD5(password+salt)</c> 计算后的哈希。
    /// </summary>
    /// <param name="request">当前密码哈希与新密码哈希。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>修改成功时返回空数据；当前密码错误时返回非零业务码。</returns>
    [HttpPost("change-password")]
    [MenuEndpoint("auth", "auth.changePassword", "修改当前用户密码")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        // 入口处做格式校验：客户端提交的必须是 32 位小写 hex。先校当前密码、再校新密码。
        if (!PasswordHashValidator.TryValidate(request.CurrentPasswordHash, out var currentError))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, currentError));
        }
        if (!PasswordHashValidator.TryValidate(request.NewPasswordHash, out var newError))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, newError));
        }

        var changed = await authService.ChangePasswordAsync(
            userId,
            request.CurrentPasswordHash,
            request.NewPasswordHash,
            cancellationToken);
        if (!changed)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, "当前密码错误或新密码不符合要求"));
        }

        DeleteRefreshCookie();
        return Ok(ApiResponse<object?>.Success(null));
    }

    private static LoginResponse ToResponse(LoginResult result) => new(
        result.AccessToken.Value,
        result.AccessToken.ExpiresAt,
        UserResponse.From(result.User));

    private void SetRefreshCookie(RefreshToken refreshToken) =>
        Response.Cookies.Append(RefreshCookieName, refreshToken.Value, CookieOptions(refreshToken.ExpiresAt));

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTimeOffset.UnixEpoch));

    private CookieOptions CookieOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        // Lax：同站 fetch（含 SPA 硬刷新）都发；跨站仅顶级导航发，仍防 CSRF。
        // Strict 在 SPA + 反向代理场景下，硬刷新触发的 fetch 有时被浏览器视为非顶级导航而不发 Cookie。
        SameSite = SameSiteMode.Lax,
        Expires = expiresAt,
        Path = "/api/auth"
    };
}

public sealed record SaltRequest([Required] string UserName);

public sealed record SaltResponse(string Salt);

public sealed record LoginRequest(
    [Required] string UserName,
    [Required] string PasswordHash);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPasswordHash,
    [Required] string NewPasswordHash);
