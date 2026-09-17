using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>
/// 用户 DAL，同时实现 <see cref="IUserDal"/> 与 <see cref="IUserCredentialDal"/>。
/// 所有凭据/角色写操作经 ASP.NET Core Identity 执行；变更走 diff 算法避免冗余 SQL。
/// 客户端提交的 <c>passwordHash</c> = <c>MD5(password+salt)</c>，服务端再用 DB 中的 salt
/// 拼接为 <c>salt:passwordHash</c> 后交给 Identity 的 PBKDF2 PasswordHasher 存储/校验。
/// </summary>
public sealed class UserDal(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    TigerRagDbContext dbContext) : IUserDal
    , IUserCredentialDal
{
    public async Task<UserAccount?> ValidateCredentialsAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrEmpty(user.PasswordSalt))
        {
            // 用户不存在 / salt 为空都视作凭据无效，避免枚举。
            return null;
        }

        // 把客户端 MD5 与服务端 salt 拼接后交给 Identity 走 PBKDF2；salt 永不离开服务端。
        var combined = PasswordSalting.Combine(user.PasswordSalt, passwordHash);
        var result = await signInManager.CheckPasswordSignInAsync(user, combined, lockoutOnFailure: true);
        return result.Succeeded ? await MapAsync(user) : null;
    }

    public async Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        return user is null || string.IsNullOrEmpty(user.PasswordSalt) ? null : user.PasswordSalt;
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
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Identity 要求 CreateAsync 必须传密码；此处先用一个随机占位密码占位。
        // SetInitialPasswordAsync 会在客户端拿到 salt 后立刻把它替换成真实 PBKDF2 哈希。
        var placeholder = PasswordSalting.GenerateSalt();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordSalt = PasswordSalting.GenerateSalt(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        EnsureSucceeded(await userManager.CreateAsync(user, placeholder));
        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            // 角色分配失败则回滚已创建的用户，避免遗留无角色账号。
            await userManager.DeleteAsync(user);
            EnsureSucceeded(roleResult);
        }

        return await MapAsync(user);
    }

    public async Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        var combined = PasswordSalting.Combine(user.PasswordSalt, passwordHash);
        // 直接走 PasswordHasher + UpdateAsync；不借道密码重置 token，避免无谓的额外依赖（token provider）。
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, combined);
        EnsureSucceeded(await userManager.UpdateAsync(user));
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        // 改密不轮换 salt；salt 与 MD5 哈希 拼接后交给 Identity，Identity 内部走 PBKDF2 校验/重哈希。
        var currentCombined = PasswordSalting.Combine(user.PasswordSalt, currentPasswordHash);
        var newCombined = PasswordSalting.Combine(user.PasswordSalt, newPasswordHash);
        var result = await userManager.ChangePasswordAsync(user, currentCombined, newCombined);
        return result.Succeeded;
    }

    public async Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        var combined = PasswordSalting.Combine(user.PasswordSalt, newPasswordHash);
        // 直接写 PasswordHasher 输出，与 ChangePasswordAsync / SetInitialPasswordAsync 同形。
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, combined);
        EnsureSucceeded(await userManager.UpdateAsync(user));
    }

    // diff 算法：desired 与 current 取差集，仅生成必要的 INSERT/DELETE。
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