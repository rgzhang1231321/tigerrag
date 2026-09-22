using Microsoft.AspNetCore.Identity;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>首次部署管理员引导。只能由 <c>--bootstrap-admin</c> 命令触发，常规启动不会创建账号。</summary>
public sealed class AdminBootstrapper(
    UserManager<AppUser> userManager,
    IUserCredentialDal credentials,
    IMenuEndpointRegistry menuEndpoints,
    IRoleEndpointGrantStore grants) : IAdminBootstrapper
{
    public async Task BootstrapAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 复用正式创建流程：先拿到随机 salt，再把 CLI 输入按 <c>MD5(password+salt)</c> 设进 PBKDF2。
        // 这样 bootstrap 管理员与通过 API 创建的用户共享同一套凭据格式。
        var created = await credentials.CreateAsync(
            userName,
            ["Admin"],
            cancellationToken);

        try
        {
            var salt = GetSalt(created) ?? throw new InvalidOperationException("新创建的用户未携带 salt，无法设密。");
            var md5 = PasswordSalting.ComputeMd5Hash(password, salt);
            await credentials.SetInitialPasswordAsync(created.Id, md5, cancellationToken);
        }
        catch
        {
            // 设密失败时回滚账号，避免留下仅持有占位密码的孤儿用户。
            var orphan = await userManager.FindByIdAsync(created.Id.ToString());
            if (orphan is not null)
            {
                await userManager.DeleteAsync(orphan);
            }

            throw;
        }

        // 为 Admin 角色灌全部 [MenuEndpoint] 授权：删除 MenuEndpointAuthFilter 的 Admin bypass 后，
        // Admin 默认全通靠这一步保证。按 endpoint 真实 MenuKey 分组灌入，让授权 UI 按菜单正确展示。
        cancellationToken.ThrowIfCancellationRequested();
        var endpoints = menuEndpoints.All;
        if (endpoints.Count > 0)
        {
            var existing = await grants.ListByRoleAsync("Admin", cancellationToken);
            var existingKeys = existing.Select(g => g.EndpointKey).ToHashSet(StringComparer.Ordinal);
            var missing = endpoints.Where(ep => !existingKeys.Contains(ep.EndpointKey)).ToList();
            if (missing.Count > 0)
            {
                foreach (var group in missing.GroupBy(ep => ep.MenuKey, StringComparer.Ordinal))
                {
                    await grants.GrantAllInMenuAsync("Admin", group.Key, group.ToList(), Guid.Empty, cancellationToken);
                }
            }
        }
    }

    // UserAccount 不暴露 salt；通过 FindByIdAsync 重新拉一次以读取 PasswordSalt 字段。
    private string? GetSalt(UserAccount account)
    {
        var user = userManager.Users.FirstOrDefault(u => u.Id == account.Id);
        return user?.PasswordSalt;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
