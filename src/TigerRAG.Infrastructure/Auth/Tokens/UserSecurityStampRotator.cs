using Microsoft.AspNetCore.Identity;
using TigerRAG.Application.Auth;
using TigerRAG.Infrastructure.Identity;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 默认按用户撤权轮换器。组合 <c>UserManager.UpdateSecurityStampAsync</c> 与
/// <see cref="IAuthRevocationCache"/>：DB 主写 + 缓存写新 stamp，两边同步。
/// Identity 的 <c>ChangePasswordAsync</c> 已自带 stamp 轮换，调用方无须额外调本类。
/// </summary>
public sealed class UserSecurityStampRotator(
    UserManager<AppUser> userManager,
    IAuthRevocationCache revocationCache) : IUserSecurityStampRotator
{
    public async Task RotateAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"SecurityStamp rotation failed: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        if (string.IsNullOrEmpty(user.SecurityStamp))
        {
            // Identity 默认必填；为空说明配置异常，主动报错而非继续写空值进缓存。
            throw new InvalidOperationException(
                $"SecurityStamp is empty after rotation for user {userId}.");
        }

        await revocationCache.SetStampAsync(userId, user.SecurityStamp, cancellationToken);
    }

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken) =>
        revocationCache.InvalidateAsync(userId, cancellationToken);
}