using TigerRAG.Application.Auth;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.UnitTests.Auth;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_WithValidCredentials_IssuesAccessToken()
    {
        var user = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]);
        var expected = new AccessToken("signed-token", DateTimeOffset.UtcNow.AddMinutes(15));
        var refresh = new RefreshToken("refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        var sessions = new RecordingRefreshSessionDal(refresh);
        var service = CreateService(user, expected, sessions);

        var result = await service.LoginAsync("admin", "client-md5-hash", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(user, result.User);
        Assert.Equal(expected, result.AccessToken);
        Assert.Equal(refresh, result.RefreshToken);
        Assert.Equal(user.Id, sessions.CreatedForUserId);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ReturnsNull()
    {
        var service = CreateService(null, null, new RecordingRefreshSessionDal(null));

        var result = await service.LoginAsync("admin", "wrong-md5-hash", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_WithValidRefreshToken_RotatesSessionAndIssuesAccessToken()
    {
        var user = new UserAccount(Guid.NewGuid(), "viewer", ["Viewer"]);
        var access = new AccessToken("new-access-token", DateTimeOffset.UtcNow.AddMinutes(15));
        var refresh = new RefreshToken("new-refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        var sessions = new RecordingRefreshSessionDal(refresh) { RotatedUser = user };
        var service = CreateService(null, access, sessions);

        var result = await service.RefreshAsync("old-refresh-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(access, result.AccessToken);
        Assert.Equal(refresh, result.RefreshToken);
        Assert.Equal("old-refresh-token", sessions.RotatedToken);
    }

    [Fact]
    public async Task LogoutAsync_RevokesRefreshToken()
    {
        var sessions = new RecordingRefreshSessionDal(null);
        var service = CreateService(null, null, sessions);

        await service.LogoutAsync("refresh-token", CancellationToken.None);

        Assert.Equal("refresh-token", sessions.RevokedToken);
    }

    [Fact]
    public async Task LogoutAsync_WhenUserExists_RecordsAudit()
    {
        var user = new UserAccount(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "viewer", ["Viewer"]);
        var sessions = new RecordingRefreshSessionDal(null) { TokenUser = user };
        var audit = new RecordingAuditWriter();
        var service = CreateService(null, null, sessions, audit: audit);

        await service.LogoutAsync("refresh-token-value", CancellationToken.None);

        Assert.Single(audit.Entries);
        var entry = audit.Entries[0];
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Equal("viewer", entry.ActorName);
        Assert.Equal(OperationAuditActions.AuthLogout, entry.Action);
        Assert.Equal("viewer 退出登录", entry.Summary);
    }

    [Fact]
    public async Task LogoutAsync_WhenUserNotFound_SkipsAudit()
    {
        var sessions = new RecordingRefreshSessionDal(null);
        var audit = new RecordingAuditWriter();
        var service = CreateService(null, null, sessions, audit: audit);

        await service.LogoutAsync("orphaned-token", CancellationToken.None);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenSuccessful_RevokesAllUserSessions()
    {
        var userId = Guid.NewGuid();
        var credentials = new RecordingUserCredentialDal(changePasswordResult: true);
        var sessions = new RecordingRefreshSessionDal(null);
        var cache = new RecordingRevocationCache();
        var service = CreateService(
            user: null,
            accessToken: null,
            sessions,
            cache,
            credentials: credentials);

        var changed = await service.ChangePasswordAsync(
            userId,
            "current-md5-hash",
            "new-md5-hash",
            CancellationToken.None);

        Assert.True(changed);
        Assert.Equal(userId, credentials.ChangedUserId);
        Assert.Equal(userId, sessions.RevokedUserId);
        // 改密后清缓存：Identity 已自动轮换 stamp，下次请求会从 DB 重新拉，避免 TTL 内继续命中旧 stamp。
        Assert.Equal(userId, cache.InvalidatedUserId);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenFailed_DoesNotTouchCache()
    {
        var userId = Guid.NewGuid();
        var credentials = new RecordingUserCredentialDal(changePasswordResult: false);
        var sessions = new RecordingRefreshSessionDal(null);
        var cache = new RecordingRevocationCache();
        var service = CreateService(
            user: null,
            accessToken: null,
            sessions,
            cache,
            credentials: credentials);

        var changed = await service.ChangePasswordAsync(
            userId,
            "current-md5-hash",
            "new-md5-hash",
            CancellationToken.None);

        Assert.False(changed);
        Assert.Null(sessions.RevokedUserId);
        Assert.Null(cache.InvalidatedUserId);
    }

    [Fact]
    public async Task LoginAsync_WarmsRevocationCache()
    {
        var user = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "stamp-1" };
        var sessions = new RecordingRefreshSessionDal(new RefreshToken("r", DateTimeOffset.UtcNow.AddDays(1)));
        var cache = new RecordingRevocationCache();
        var service = CreateService(
            user,
            new AccessToken("a", DateTimeOffset.UtcNow.AddMinutes(15)),
            sessions,
            cache);

        var result = await service.LoginAsync("admin", "client-md5", CancellationToken.None);

        Assert.NotNull(result);
        // 登录时预热 stamp 缓存，让登录后的第一次请求直接命中，无需回退到 DB。
        Assert.Equal(user.Id, cache.WarmedUserId);
        Assert.Equal("stamp-1", cache.WarmedStamp);
    }

    [Fact]
    public async Task LoginAsync_WhenCacheThrows_StillReturnsToken()
    {
        var user = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "stamp-1" };
        var sessions = new RecordingRefreshSessionDal(new RefreshToken("r", DateTimeOffset.UtcNow.AddDays(1)));
        var cache = new ThrowingRevocationCache();
        var service = CreateService(
            user,
            new AccessToken("a", DateTimeOffset.UtcNow.AddMinutes(15)),
            sessions,
            cache);

        var result = await service.LoginAsync("admin", "client-md5", CancellationToken.None);

        // 缓存写失败不能阻塞登录：stamp 比较路径会回退到 DB 正确判 stamp。
        Assert.NotNull(result);
    }

    private static AuthService CreateService(
        UserAccount? user,
        AccessToken? accessToken,
        RecordingRefreshSessionDal sessions,
        IAuthRevocationCache? cache = null,
        RecordingUserCredentialDal? credentials = null,
        RecordingAuditWriter? audit = null) => new(
            new StubUserDal(user),
            credentials ?? new RecordingUserCredentialDal(false),
            new StubTokenIssuer(accessToken),
            sessions,
            cache ?? new RecordingRevocationCache(),
            audit ?? new RecordingAuditWriter());

    private sealed class RecordingAuditWriter : IOperationAuditWriter
    {
        public List<OperationAuditEntry> Entries { get; } = [];

        public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubUserDal(UserAccount? user) : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string passwordHash,
            CancellationToken cancellationToken) => Task.FromResult(user);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AssignRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubTokenIssuer(AccessToken? token) : IAccessTokenIssuer
    {
        public Task<AccessToken> IssueAsync(UserAccount user, CancellationToken cancellationToken) =>
            token is null
                ? throw new InvalidOperationException("Token must not be issued.")
                : Task.FromResult(token);
    }

    private sealed class RecordingUserCredentialDal(bool changePasswordResult) : IUserCredentialDal
    {
        public Guid? ChangedUserId { get; private set; }

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string currentPasswordHash,
            string newPasswordHash,
            CancellationToken cancellationToken)
        {
            ChangedUserId = userId;
            return Task.FromResult(changePasswordResult);
        }

        public Task<UserAccount> CreateAsync(
            string userName,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetInitialPasswordAsync(
            Guid userId,
            string passwordHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResetPasswordAsync(
            Guid userId,
            string newPasswordHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetLockoutAsync(
            Guid userId,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRefreshSessionDal(RefreshToken? token) : IRefreshSessionDal
    {
        public Guid? CreatedForUserId { get; private set; }
        public UserAccount? RotatedUser { get; init; }
        public UserAccount? TokenUser { get; init; }
        public string? RotatedToken { get; private set; }
        public string? RevokedToken { get; private set; }
        public Guid? RevokedUserId { get; private set; }

        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken)
        {
            CreatedForUserId = userId;
            return Task.FromResult(token!);
        }

        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
        {
            RotatedToken = value;
            return Task.FromResult(RotatedUser is null || token is null
                ? null
                : new RefreshSession(RotatedUser, token));
        }

        public Task RevokeAsync(string value, CancellationToken cancellationToken)
        {
            RevokedToken = value;
            return Task.CompletedTask;
        }

        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
        {
            RevokedUserId = userId;
            return Task.CompletedTask;
        }

        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult(TokenUser);
    }

    private sealed class RecordingRevocationCache : IAuthRevocationCache
    {
        public Guid? WarmedUserId { get; private set; }
        public string? WarmedStamp { get; private set; }
        public Guid? InvalidatedUserId { get; private set; }

        public Task<string?> GetStampAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public Task SetStampAsync(Guid userId, string stamp, CancellationToken ct)
        {
            WarmedUserId = userId;
            WarmedStamp = stamp;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(Guid userId, CancellationToken ct)
        {
            InvalidatedUserId = userId;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRevocationCache : IAuthRevocationCache
    {
        public Task<string?> GetStampAsync(Guid userId, CancellationToken ct) =>
            throw new InvalidOperationException("cache down");

        public Task SetStampAsync(Guid userId, string stamp, CancellationToken ct) =>
            throw new InvalidOperationException("cache down");

        public Task InvalidateAsync(Guid userId, CancellationToken ct) =>
            throw new InvalidOperationException("cache down");
    }
}

