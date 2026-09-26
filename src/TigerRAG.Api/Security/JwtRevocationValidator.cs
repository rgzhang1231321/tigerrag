using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Users;

namespace TigerRAG.Api.Security;

/// <summary>
/// JWT 撤权校验器：每个已签名请求进 OnTokenValidated 时把 claim 中的 security_stamp
/// 与缓存（Redis L1）/DB 实时值比对；用户不存在、stamp 不匹配、或已锁定均 <c>context.Fail</c>。
/// 缓存不可达由缓存实现内部处理（记录日志 + 按 miss 处理），本校验器直接走 DB 安全路径。
/// </summary>
public static class JwtRevocationValidator
{
    public const string SecurityStampClaim = "security_stamp";

    public static Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("Missing principal.");
            return Task.CompletedTask;
        }

        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("TigerRAG.Api.Security.JwtRevocationValidator");

        try
        {
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(sub, out var userId))
            {
                context.Fail("Invalid subject claim.");
                return Task.CompletedTask;
            }

            var claimStamp = principal.FindFirst(SecurityStampClaim)?.Value;
            if (string.IsNullOrEmpty(claimStamp))
            {
                // 部署前的旧 token 不带 security_stamp claim；强制失效走重登路径。
                context.Fail("Missing security_stamp claim.");
                return Task.CompletedTask;
            }

            return ValidateAsync(context, services, userId, claimStamp, logger);
        }
        catch (Exception error)
        {
            logger.LogError(error, "JWT revocation validator failed.");
            context.Fail("Auth pipeline error.");
            return Task.CompletedTask;
        }
    }

    private static async Task ValidateAsync(
        TokenValidatedContext context,
        IServiceProvider services,
        Guid userId,
        string claimStamp,
        ILogger logger)
    {
        var cache = services.GetRequiredService<IAuthRevocationCache>();
        var users = services.GetRequiredService<IUserDal>();

        // 缓存不可达由实现内部处理（记录日志 + 返回 null）；此处 null 等价于 miss，直接走 DB 安全路径。
        var cached = await cache.GetStampAsync(userId, context.HttpContext.RequestAborted);

        string currentStamp;
        bool isLocked;
        if (cached is not null)
        {
            currentStamp = cached;
            // stamp 命中缓存仍要查锁口：缓存只缓存 stamp，锁口状态由 DB 实时值决定。
            isLocked = await IsLockedAsync(users, userId, logger);
        }
        else
        {
            var snapshot = await users.GetRevocationSnapshotAsync(userId, context.HttpContext.RequestAborted);
            if (snapshot is null)
            {
                context.Fail("User no longer exists.");
                return;
            }

            currentStamp = snapshot.SecurityStamp;
            isLocked = snapshot.IsLocked;

            // 回填缓存；写入失败由实现内部记录日志并吞掉，不影响本次请求。
            await cache.SetStampAsync(userId, currentStamp, context.HttpContext.RequestAborted);
        }

        if (!string.Equals(claimStamp, currentStamp, StringComparison.Ordinal))
        {
            context.Fail("Security stamp mismatch.");
            return;
        }

        if (isLocked)
        {
            context.Fail("User is locked out.");
        }
    }

    private static async Task<bool> IsLockedAsync(IUserDal users, Guid userId, ILogger logger)
    {
        try
        {
            var snapshot = await users.GetRevocationSnapshotAsync(userId, CancellationToken.None);
            return snapshot?.IsLocked ?? false;
        }
        catch (Exception error)
        {
            // 锁口查询失败按"未锁"处理：避免单点 DB 抖动导致全员误锁定。
            // stamp 已通过即可正常通过业务校验，攻击者也无法借此次失败绕过 lockout——因为
            // stamp 不匹配或用户被删已被前置步骤拦截。
            logger.LogWarning(error, "Lockout check failed; treating as unlocked.");
            return false;
        }
    }
}
