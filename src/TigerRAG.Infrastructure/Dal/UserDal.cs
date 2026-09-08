using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

public sealed class UserDal(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    TigerRagDbContext dbContext) : IUserDal
    , IUserCredentialDal
{
    public async Task<UserAccount?> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        return result.Succeeded ? await MapAsync(user) : null;
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(user => user.UserName).ToListAsync(cancellationToken);
        var result = new List<UserAccount>(users.Count);
        foreach (var user in users)
        {
            result.Add(await MapAsync(user));
        }

        return result;
    }

    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            throw new KeyNotFoundException($"User {userId} was not found.");
        }

        var roleIds = await dbContext.Roles
            .Where(role => role.Name != null && roles.Contains(role.Name))
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);
        if (roleIds.Length != roles.Count)
        {
            throw new InvalidOperationException("One or more system roles are missing from the database.");
        }

        var currentRoles = await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ToArrayAsync(cancellationToken);
        ApplyRoleChanges(dbContext, userId, currentRoles, roleIds);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserAccount> CreateAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = userName };
        EnsureSucceeded(await userManager.CreateAsync(user, password));
        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            EnsureSucceeded(roleResult);
        }

        return await MapAsync(user);
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return result.Succeeded;
    }

    public async Task ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        EnsureSucceeded(await userManager.ResetPasswordAsync(user, token, newPassword));
    }

    internal static void ApplyRoleChanges(
        TigerRagDbContext dbContext,
        Guid userId,
        IReadOnlyCollection<IdentityUserRole<Guid>> currentRoles,
        IReadOnlyCollection<Guid> desiredRoleIds)
    {
        var desired = desiredRoleIds.ToHashSet();
        var current = currentRoles.Select(userRole => userRole.RoleId).ToHashSet();

        dbContext.UserRoles.RemoveRange(currentRoles.Where(userRole => !desired.Contains(userRole.RoleId)));
        dbContext.UserRoles.AddRange(desired
            .Where(roleId => !current.Contains(roleId))
            .Select(roleId => new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId }));
    }

    private async Task<UserAccount> MapAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserAccount(user.Id, user.UserName ?? string.Empty, roles.ToArray());
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
