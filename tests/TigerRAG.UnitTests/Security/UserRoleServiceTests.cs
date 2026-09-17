using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

public sealed class UserRoleServiceTests
{
    private static RecordingMenuConfigDal MenuConfigs = new();

    [Fact]
    public async Task AssignRolesAsync_WithUnknownRole_RejectsRequest()
    {
        var dal = new RecordingUserDal();
        var service = CreateService(dal);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AssignRolesAsync(Guid.NewGuid(), ["SuperUser"], CancellationToken.None));

        Assert.Contains("SuperUser", error.Message);
        Assert.Null(dal.AssignedRoles);
    }

    [Fact]
    public async Task AssignRolesAsync_WithKnownRoles_RemovesDuplicates()
    {
        var dal = new RecordingUserDal();
        var service = CreateService(dal);

        await service.AssignRolesAsync(
            Guid.NewGuid(),
            [SystemRoles.Viewer, SystemRoles.Viewer, SystemRoles.Editor],
            CancellationToken.None);

        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], dal.AssignedRoles);
    }

    [Fact]
    public async Task CreateAsync_WithKnownRoles_NormalizesRoles()
    {
        var credentials = new RecordingCredentialDal();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            new RecordingRefreshSessionDal(),
            MenuConfigs);

        var user = await service.CreateAsync(
            "new-user",
            [SystemRoles.Viewer, SystemRoles.Editor, SystemRoles.Viewer],
            CancellationToken.None);

        Assert.Equal("new-user", user.UserName);
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], credentials.CreatedRoles);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_ForwardsHashAndRevokesAllSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            sessions,
            MenuConfigs);
        var userId = Guid.NewGuid();

        await service.SetInitialPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.SetInitialPasswordUserId);
        Assert.Equal("client-md5-hash", credentials.SetInitialPasswordHash);
        Assert.Equal(userId, sessions.RevokedUserId);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_WhenCredentialFails_DoesNotRevoke()
    {
        var credentials = new ThrowingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            sessions,
            MenuConfigs);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetInitialPasswordAsync(Guid.NewGuid(), "client-md5-hash", CancellationToken.None));

        Assert.Null(sessions.RevokedUserId);
    }

    [Fact]
    public async Task ResetPasswordAsync_RevokesAllTargetUserSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(new RecordingUserDal(), credentials, sessions, MenuConfigs);
        var userId = Guid.NewGuid();

        await service.ResetPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.ResetUserId);
        Assert.Equal(userId, sessions.RevokedUserId);
    }

    [Fact]
    public async Task DeleteAsync_RevokesAllSessionsThenDeletes()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(new RecordingUserDal(), credentials, sessions, MenuConfigs);
        var userId = Guid.NewGuid();

        var deleted = await service.DeleteAsync(userId, CancellationToken.None);

        Assert.True(deleted);
        // 先撤销会话、再删账号：顺序很关键，避免删完账号后会话孤立。
        Assert.Equal(userId, sessions.RevokedUserId);
        Assert.Equal(userId, credentials.DeletedUserId);
    }

    [Fact]
    public async Task SetLockoutAsync_WhenLocking_RevokesAllSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(new RecordingUserDal(), credentials, sessions, MenuConfigs);
        var userId = Guid.NewGuid();
        var lockoutEnd = DateTimeOffset.UtcNow.AddYears(100);

        await service.SetLockoutAsync(userId, lockoutEnd, CancellationToken.None);

        Assert.Equal(userId, credentials.LockoutUserId);
        Assert.Equal(lockoutEnd, credentials.LockoutEnd);
        Assert.Equal(userId, sessions.RevokedUserId);
    }

    [Fact]
    public async Task SetLockoutAsync_WhenUnlocking_DoesNotRevoke()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(new RecordingUserDal(), credentials, sessions, MenuConfigs);

        await service.SetLockoutAsync(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Null(sessions.RevokedUserId);
    }

    private static UserRoleService CreateService(IUserDal users) => new(
        users,
        new RecordingCredentialDal(),
        new RecordingRefreshSessionDal(),
        MenuConfigs);

    private sealed class RecordingMenuConfigDal : IMenuConfigDal
    {
        public Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MenuConfigItem>>([]);

        public Task<MenuConfigItem> CreateAsync(
            string key,
            string label,
            string? icon,
            string? permission,
            Guid? parentId,
            int sortOrder,
            bool isEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MenuConfigItem(Guid.NewGuid(), key, label, icon, permission, parentId, sortOrder, isEnabled));

        public Task<MenuConfigItem?> UpdateAsync(
            Guid id,
            string? label,
            string? icon,
            string? permission,
            Guid? parentId,
            int? sortOrder,
            bool? isEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult<MenuConfigItem?>(new MenuConfigItem(id, "stub", label ?? "label", icon, permission, parentId, sortOrder ?? 0, isEnabled ?? true));

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingUserDal : IUserDal
    {
        public IReadOnlyCollection<string>? AssignedRoles { get; private set; }

        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string passwordHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AssignRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            AssignedRoles = roles;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCredentialDal : IUserCredentialDal
    {
        public IReadOnlyCollection<string>? CreatedRoles { get; private set; }
        public Guid? ResetUserId { get; private set; }
        public Guid? SetInitialPasswordUserId { get; private set; }
        public string? SetInitialPasswordHash { get; private set; }

        public Task<UserAccount> CreateAsync(
            string userName,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            CreatedRoles = roles;
            return Task.FromResult(new UserAccount(Guid.NewGuid(), userName, roles.ToArray()));
        }

        public Task SetInitialPasswordAsync(
            Guid userId,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            SetInitialPasswordUserId = userId;
            SetInitialPasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string currentPasswordHash,
            string newPasswordHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResetPasswordAsync(
            Guid userId,
            string newPasswordHash,
            CancellationToken cancellationToken)
        {
            ResetUserId = userId;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
        {
            DeletedUserId = userId;
            return Task.FromResult(true);
        }

        public Task SetLockoutAsync(
            Guid userId,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken)
        {
            LockoutUserId = userId;
            LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Guid? DeletedUserId { get; private set; }
        public Guid? LockoutUserId { get; private set; }
        public DateTimeOffset? LockoutEnd { get; private set; }
    }

    private sealed class RecordingRefreshSessionDal : IRefreshSessionDal
    {
        public Guid? RevokedUserId { get; private set; }

        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
        {
            RevokedUserId = userId;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCredentialDal : IUserCredentialDal
    {
        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("credential write failed");

        public Task<bool> ChangePasswordAsync(Guid userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ResetPasswordAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetLockoutAsync(Guid userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
