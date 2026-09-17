using Microsoft.AspNetCore.Identity;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Dal;

namespace TigerRAG.Infrastructure.Identity;

/// <summary>首次部署管理员引导。只能由 <c>--bootstrap-admin</c> 命令触发，常规启动不会创建账号。</summary>
public sealed class AdminBootstrapper(
    UserManager<AppUser> userManager,
    IUserCredentialDal credentials) : IAdminBootstrapper
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
            [SystemRoles.Admin],
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