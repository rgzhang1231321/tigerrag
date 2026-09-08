using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    private const string RefreshCookieName = "tigerrag.refresh";

    /// <summary>
    /// 使用用户名和密码登录，返回访问令牌，并通过 HttpOnly Cookie 写入刷新令牌。
    /// </summary>
    /// <param name="request">登录用户名和密码。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>登录成功时返回访问令牌和当前用户信息；凭据无效时返回非零业务码。</returns>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request.UserName, request.Password, cancellationToken);
        if (result is null)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Unauthorized, "用户名或密码错误"));
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
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Unauthorized, "缺少刷新令牌"));
        }

        var result = await authService.RefreshAsync(refreshToken, cancellationToken);
        if (result is null)
        {
            DeleteRefreshCookie();
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Unauthorized, "刷新令牌无效或已过期"));
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
    /// </summary>
    /// <param name="request">当前密码和符合安全要求的新密码。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>修改成功时返回空数据；当前密码错误或新密码不合规时返回非零业务码。</returns>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Unauthorized, "用户身份无效"));
        }

        var changed = await authService.ChangePasswordAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        if (!changed)
        {
            return Ok(ApiResponse<object?>.Failure(ApiErrorCodes.Validation, "当前密码错误或新密码不符合要求"));
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
        SameSite = SameSiteMode.Strict,
        Expires = expiresAt,
        Path = "/api/auth"
    };
}

public sealed record LoginRequest(
    [Required] string UserName,
    [Required] string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(10)] string NewPassword);
