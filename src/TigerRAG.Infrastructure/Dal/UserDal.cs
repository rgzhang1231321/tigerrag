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
        // 入口处先做格式校验：客户端提交的必须是 32 位小写 hex。与 ChangePasswordAsync / SetInitialPasswordAsync / ResetPasswordAsync 同形。
        PasswordHashFormat.EnsureAcceptable(passwordHash);
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrEmpty(user.PasswordSalt))
        {
            // 用户不存在 / salt 为空都视作凭据无效，避免枚举。
            return null;
        }

        // 把客户端 MD5 与服务端 salt 拼接后交给 Identity 走 PBKDF2；salt 永不离开服务端。
        var combined = PasswordSalting.Combine(user.PasswordSalt, passwordHash);
        var result = await signInManager.CheckPasswordSignInAsync(user, combined, lockoutOnFailure: true);
        return result.Succeeded ? ToAccount(await MapAsync(user)) : null;
    }

    public async Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        return user is null || string.IsNullOrEmpty(user.PasswordSalt) ? null : user.PasswordSalt;
    }

    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(user => user.UserName).ToListAsync(cancellationToken);
        var result = new List<UserListItem>(users.Count);
        foreach (var user in users)
        {
            result.Add(await MapAsync(user));
        }

        return result;
    }

    public async Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        // 仅查 stamp 与锁口状态，每请求路径上避免触发完整 MapAsync 的角色查询。
        var stamp = await userManager.GetSecurityStampAsync(user);
        var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
        return new RevocationSnapshot(stamp ?? string.Empty, isLocked);
    }

    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 父行行锁：把同一用户的并发覆盖式更新串行化，避免读旧集 → 计算差集 → 保存的窗口出现并集残留。
        // 默认 READ COMMITTED 下 SELECT FOR UPDATE 会阻塞其他写者，直到本事务提交/回滚。
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $""" SELECT 1 FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE """,
            cancellationToken);

        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            await tx.RollbackAsync(cancellationToken);
            throw new KeyNotFoundException($"User {userId} was not found.");
        }

        var roleIds = await dbContext.Roles
            .Where(role => role.Name != null && roles.Contains(role.Name))
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);
        if (roleIds.Length != roles.Count)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("One or more system roles are missing from the database.");
        }

        var currentRoles = await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ToArrayAsync(cancellationToken);
        ApplyRoleChanges(dbContext, userId, currentRoles, roleIds);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
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

        return ToAccount(await MapAsync(user));
    }

    public async Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 入口处先做格式校验：客户端提交的必须是 32 位小写 hex，否则会被原样入 PBKDF2。
        PasswordHashFormat.EnsureAcceptable(passwordHash);
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
        // 入口处先做格式校验：客户端提交的必须是 32 位小写 hex。与 SetInitialPasswordAsync / ResetPasswordAsync 同形。
        PasswordHashFormat.EnsureAcceptable(currentPasswordHash);
        PasswordHashFormat.EnsureAcceptable(newPasswordHash);
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
        // 入口处先做格式校验：客户端提交的必须是 32 位小写 hex，否则会被原样入 PBKDF2。
        PasswordHashFormat.EnsureAcceptable(newPasswordHash);
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

    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }
        // 级联删除由 DB 外键约束承担；Identity 删除会清理 UserRoles / UserClaims / UserLogins。
        EnsureSucceeded(await userManager.DeleteAsync(user));
        return true;
    }

    public async Task SetLockoutAsync(
        Guid userId,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, lockoutEnd));
    }

    private async Task<UserListItem> MapAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
        // Identity 默认在创建用户时即初始化 SecurityStamp；之后由 UpdateSecurityStampAsync 轮换。
        return new UserListItem(user.Id, user.UserName ?? string.Empty, roles.ToArray(), isLocked)
        {
            SecurityStamp = user.SecurityStamp ?? string.Empty
        };
    }

    private static UserAccount ToAccount(UserListItem item) =>
        new(item.Id, item.UserName, item.Roles) { SecurityStamp = item.SecurityStamp };

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}