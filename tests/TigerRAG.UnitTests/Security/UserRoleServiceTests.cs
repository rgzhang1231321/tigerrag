using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

public sealed class UserRoleServiceTests
{
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
            new RecordingRefreshSessionDal());

        var user = await service.CreateAsync(
            "new-user",
            [SystemRoles.Viewer, SystemRoles.Editor, SystemRoles.Viewer],
            CancellationToken.None);

        Assert.Equal("new-user", user.UserName);
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], credentials.CreatedRoles);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_ForwardsHashToCredentialDal()
    {
        var credentials = new RecordingCredentialDal();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            new RecordingRefreshSessionDal());
        var userId = Guid.NewGuid();

        await service.SetInitialPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.SetInitialPasswordUserId);
        Assert.Equal("client-md5-hash", credentials.SetInitialPasswordHash);
    }

    [Fact]
    public async Task ResetPasswordAsync_RevokesAllTargetUserSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var service = new UserRoleService(new RecordingUserDal(), credentials, sessions);
        var userId = Guid.NewGuid();

        await service.ResetPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.ResetUserId);
        Assert.Equal(userId, sessions.RevokedUserId);
    }

    private static UserRoleService CreateService(IUserDal users) => new(
        users,
        new RecordingCredentialDal(),
        new RecordingRefreshSessionDal());

    private sealed class RecordingUserDal : IUserDal
    {
        public IReadOnlyCollection<string>? AssignedRoles { get; private set; }

        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string passwordHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
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
}
