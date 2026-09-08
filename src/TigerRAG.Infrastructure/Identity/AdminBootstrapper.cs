using Microsoft.AspNetCore.Identity;
using TigerRAG.Application.Security;

namespace TigerRAG.Infrastructure.Identity;

public sealed class AdminBootstrapper(UserManager<AppUser> userManager) : IAdminBootstrapper
{
    public async Task BootstrapAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new AppUser { Id = Guid.NewGuid(), UserName = userName };
            EnsureSucceeded(await userManager.CreateAsync(user, password));
        }

        if (!await userManager.IsInRoleAsync(user, SystemRoles.Admin))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, SystemRoles.Admin));
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
