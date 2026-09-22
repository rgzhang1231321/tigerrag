using TigerRAG.Application.Auth;
using TigerRAG.Application.Menus;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.UnitTests.Users;

public sealed class UserRoleServiceTests
{
    private static RecordingMenuConfigDal MenuConfigs = new();

    [Fact]
    public async Task AssignRolesAsync_WithUnknownRole_RejectsRequest()
    {
        var dal = new RecordingUserDal();
        var service = CreateService(dal);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AssignRolesAsync(Guid.NewGuid(), Guid.NewGuid(), ["SuperUser"], CancellationToken.None));

        Assert.Contains("SuperUser", error.Message);
        Assert.Null(dal.AssignedRoles);
    }

    [Fact]
    public async Task AssignRolesAsync_WithKnownRoles_RemovesDuplicates()
    {
        var dal = new RecordingUserDal();
        var service = CreateService(dal);
        var userId = Guid.NewGuid();

        await service.AssignRolesAsync(
            userId,
            userId,
            ["Viewer", "Viewer", "Editor"],
            CancellationToken.None);

        Assert.Equal(["Editor", "Viewer"], dal.AssignedRoles);
    }

    [Fact]
    public async Task CreateAsync_WithKnownRoles_NormalizesRoles()
    {
        var credentials = new RecordingCredentialDal();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            new RecordingRefreshSessionDal(),
            MenuConfigs,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin().Seed("Viewer").Seed("Editor"),
            new RecordingRoleRegistry().Seed("Viewer").Seed("Editor"));

        var user = await service.CreateAsync(
            "new-user",
            ["Viewer", "Editor", "Viewer"],
            CancellationToken.None);

        Assert.Equal("new-user", user.UserName);
        Assert.Equal(["Editor", "Viewer"], credentials.CreatedRoles);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_ForwardsHashAndRevokesAllSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            sessions,
            MenuConfigs,
            rotator,
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin(),
            new RecordingRoleRegistry());
        var userId = Guid.NewGuid();

        await service.SetInitialPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.SetInitialPasswordUserId);
        Assert.Equal("client-md5-hash", credentials.SetInitialPasswordHash);
        Assert.Equal(userId, sessions.RevokedUserId);
        Assert.Equal(userId, rotator.RotatedUserId);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_WhenCredentialFails_DoesNotRevoke()
    {
        var credentials = new ThrowingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(),
            credentials,
            sessions,
            MenuConfigs,
            rotator,
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin(),
            new RecordingRoleRegistry());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetInitialPasswordAsync(Guid.NewGuid(), "client-md5-hash", CancellationToken.None));

        Assert.Null(sessions.RevokedUserId);
        Assert.Null(rotator.RotatedUserId);
    }

    [Fact]
    public async Task ResetPasswordAsync_RevokesAllTargetUserSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(), credentials, sessions, MenuConfigs, rotator, new RecordingUnitOfWork(), new RecordingRoleAdmin(), new RecordingRoleRegistry());
        var userId = Guid.NewGuid();

        await service.ResetPasswordAsync(userId, "client-md5-hash", CancellationToken.None);

        Assert.Equal(userId, credentials.ResetUserId);
        Assert.Equal(userId, sessions.RevokedUserId);
        Assert.Equal(userId, rotator.RotatedUserId);
    }

    [Fact]
    public async Task DeleteAsync_RevokesAllSessionsThenDeletes()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(), credentials, sessions, MenuConfigs, rotator, new RecordingUnitOfWork(), new RecordingRoleAdmin(), new RecordingRoleRegistry());
        var userId = Guid.NewGuid();

        var deleted = await service.DeleteAsync(userId, CancellationToken.None);

        Assert.True(deleted);
        // 先撤销会话、再删账号：顺序很关键，避免删完账号后会话孤立。
        Assert.Equal(userId, sessions.RevokedUserId);
        Assert.Equal(userId, credentials.DeletedUserId);
        Assert.Equal(userId, rotator.InvalidatedUserId);
    }

    [Fact]
    public async Task SetLockoutAsync_WhenLocking_RevokesAllSessions()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(), credentials, sessions, MenuConfigs, rotator, new RecordingUnitOfWork(), new RecordingRoleAdmin(), new RecordingRoleRegistry());
        var userId = Guid.NewGuid();
        var lockoutEnd = DateTimeOffset.UtcNow.AddYears(100);

        await service.SetLockoutAsync(userId, lockoutEnd, CancellationToken.None);

        Assert.Equal(userId, credentials.LockoutUserId);
        Assert.Equal(lockoutEnd, credentials.LockoutEnd);
        Assert.Equal(userId, sessions.RevokedUserId);
        Assert.Equal(userId, rotator.RotatedUserId);
    }

    [Fact]
    public async Task SetLockoutAsync_WhenUnlocking_DoesNotRevoke()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(), credentials, sessions, MenuConfigs, rotator, new RecordingUnitOfWork(), new RecordingRoleAdmin(), new RecordingRoleRegistry());

        await service.SetLockoutAsync(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Null(sessions.RevokedUserId);
        Assert.Null(rotator.RotatedUserId);
    }

    [Fact]
    public async Task AssignRolesAsync_RotatesStampAndRevokesAllSessions()
    {
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = new UserRoleService(
            new RecordingUserDal(),
            new RecordingCredentialDal(),
            sessions,
            MenuConfigs,
            rotator,
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin().Seed("Viewer"),
            new RecordingRoleRegistry().Seed("Viewer"));
        var userId = Guid.NewGuid();

        await service.AssignRolesAsync(userId, userId, ["Viewer"], CancellationToken.None);

        // 角色变更需要让旧 JWT 立刻失效：stamp 轮换 + refresh 全量撤销。
        Assert.Equal(userId, rotator.RotatedUserId);
        Assert.Equal(userId, sessions.RevokedUserId);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_WhenStampRotationFails_UnitOfWorkRollsBack()
    {
        var credentials = new RecordingCredentialDal();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new ThrowingStampRotator();
        var uow = new RecordingUnitOfWork();
        var service = new UserRoleService(
            new RecordingUserDal(), credentials, sessions, MenuConfigs, rotator, uow, new RecordingRoleAdmin(), new RecordingRoleRegistry());
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetInitialPasswordAsync(userId, "client-md5-hash", CancellationToken.None));

        // UoW 被调用一次，且操作抛异常——真实实现会整体回滚，避免密码已改而 stamp/refresh 未动。
        Assert.Equal(1, uow.ExecuteCount);
        Assert.True(uow.LastOperationThrew);
        // 异常在 RotateAsync 处抛出，RevokeAllAsync 未被触及。
        Assert.Null(sessions.RevokedUserId);
    }

    [Fact]
    public async Task AssignRolesAsync_WithCustomRoleInDb_AcceptsRole()
    {
        // 自定义角色只要在 DB 存在即可分配。
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var registry = new RecordingRoleRegistry().Seed("CustomRole");
        var dal = new RecordingUserDal();
        var service = new UserRoleService(
            dal,
            new RecordingCredentialDal(),
            new RecordingRefreshSessionDal(),
            MenuConfigs,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            roleAdmin,
            registry);

        await service.AssignRolesAsync(Guid.NewGuid(), Guid.NewGuid(), ["CustomRole"], CancellationToken.None);

        Assert.Equal(["CustomRole"], dal.AssignedRoles);
    }

    [Fact]
    public async Task AssignRolesAsync_WithCustomRoleNotInDb_RejectsRequest()
    {
        var roleAdmin = new RecordingRoleAdmin(); // 空 stub，未 seed 任何角色
        var registry = new RecordingRoleRegistry();
        var dal = new RecordingUserDal();
        var service = new UserRoleService(
            dal,
            new RecordingCredentialDal(),
            new RecordingRefreshSessionDal(),
            MenuConfigs,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            roleAdmin,
            registry);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AssignRolesAsync(Guid.NewGuid(), Guid.NewGuid(), ["GhostRole"], CancellationToken.None));

        Assert.Contains("GhostRole", error.Message);
        Assert.Null(dal.AssignedRoles);
    }

    [Fact]
    public async Task ListMenuForRolesAsync_FiltersByRoleUnion()
    {
        // 三条菜单：一条对所有人开放、一条仅 Viewer、一条仅 Admin；并集按角色过滤 + 仅启用项。
        var menuDal = new StubMenuConfigDal([
            new MenuConfigItem(Guid.NewGuid(), "public", "公开", null, [], null, 0, true),
            new MenuConfigItem(Guid.NewGuid(), "viewer-only", "Viewer专属", null, ["Viewer"], null, 1, true),
            new MenuConfigItem(Guid.NewGuid(), "admin-only", "Admin专属", null, ["Admin"], null, 2, true),
            new MenuConfigItem(Guid.NewGuid(), "disabled", "已禁用", null, [], null, 3, false),
        ]);
        var service = new UserRoleService(
            new RecordingUserDal(),
            new RecordingCredentialDal(),
            new RecordingRefreshSessionDal(),
            menuDal,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin(),
            new RecordingRoleRegistry());

        var items = await service.ListMenuForRolesAsync(["Viewer", "Editor"], CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.Key == "public");
        Assert.Contains(items, item => item.Key == "viewer-only");
        Assert.DoesNotContain(items, item => item.Key == "admin-only");
        Assert.DoesNotContain(items, item => item.Key == "disabled");
    }

    [Fact]
    public async Task ListMenuForRolesAsync_WithEmptyRoles_OnlyReturnsPublicItems()
    {
        // 空角色集合（匿名调用）只返回 Roles=[] 的菜单；禁用项被排除。
        var menuDal = new StubMenuConfigDal([
            new MenuConfigItem(Guid.NewGuid(), "public", "公开", null, [], null, 0, true),
            new MenuConfigItem(Guid.NewGuid(), "viewer-only", "Viewer专属", null, ["Viewer"], null, 1, true),
        ]);
        var service = new UserRoleService(
            new RecordingUserDal(),
            new RecordingCredentialDal(),
            new RecordingRefreshSessionDal(),
            menuDal,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            new RecordingRoleAdmin(),
            new RecordingRoleRegistry());

        var items = await service.ListMenuForRolesAsync([], CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("public", items[0].Key);
    }

    [Fact]
    public async Task AssignRolesAsync_WhenSelfDemotingLastAdmin_RejectsRequest()
    {
        // 操作者即目标用户，且是最后一名 Admin 持有者 → 拒绝自我降级。
        var roleAdmin = new RecordingRoleAdmin().Seed("Admin").Seed("Viewer");
        var registry = new RecordingRoleRegistry().Seed("Admin").Seed("Viewer").SetUserIsAdmin(true).SetHolders(1);
        var dal = new RecordingUserDal();
        var service = new UserRoleService(
            dal,
            new RecordingCredentialDal(),
            new RecordingRefreshSessionDal(),
            MenuConfigs,
            new RecordingStampRotator(),
            new RecordingUnitOfWork(),
            roleAdmin,
            registry);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AssignRolesAsync(Guid.Empty, Guid.Empty, ["Viewer"], CancellationToken.None));

        Assert.Contains("Admin", error.Message);
        Assert.Null(dal.AssignedRoles);
    }

    private static UserRoleService CreateService(IUserDal users) => new(
        users,
        new RecordingCredentialDal(),
        new RecordingRefreshSessionDal(),
        MenuConfigs,
        new RecordingStampRotator(),
        new RecordingUnitOfWork(),
        new RecordingRoleAdmin().Seed("Viewer").Seed("Editor").Seed("Admin"),
        new RecordingRoleRegistry().Seed("Viewer").Seed("Editor").Seed("Admin"));


    private sealed class RecordingMenuConfigDal : IMenuConfigDal
    {
        public Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MenuConfigItem>>([]);

        public Task<MenuConfigItem> CreateAsync(
            string key,
            string label,
            string? icon,
            IReadOnlyCollection<string> roles,
            Guid? parentId,
            int sortOrder,
            bool isEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MenuConfigItem(Guid.NewGuid(), key, label, icon, roles.ToArray(), parentId, sortOrder, isEnabled));

        public Task<MenuConfigItem?> UpdateAsync(
            Guid id,
            FieldUpdate<string> label,
            FieldUpdate<string> icon,
            FieldUpdate<IReadOnlyCollection<string>> roles,
            FieldUpdate<Guid?> parentId,
            FieldUpdate<int> sortOrder,
            FieldUpdate<bool> isEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult<MenuConfigItem?>(new MenuConfigItem(
                id, "stub",
                label.HasValue ? label.Value! : "label",
                icon.HasValue ? icon.Value : null,
                roles.HasValue ? roles.Value.ToArray() : Array.Empty<string>(),
                parentId.HasValue ? parentId.Value : null,
                sortOrder.HasValue ? sortOrder.Value : 0,
                isEnabled.HasValue ? isEnabled.Value : true));

        public Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(1);
    }

    /// <summary>可预设返回数据的菜单 DAL 测试替身。</summary>
    private sealed class StubMenuConfigDal : IMenuConfigDal
    {
        private readonly IReadOnlyList<MenuConfigItem> _items;

        public StubMenuConfigDal(IReadOnlyList<MenuConfigItem> items) => _items = items;

        public Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_items);

        public Task<MenuConfigItem> CreateAsync(
            string key,
            string label,
            string? icon,
            IReadOnlyCollection<string> roles,
            Guid? parentId,
            int sortOrder,
            bool isEnabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MenuConfigItem?> UpdateAsync(
            Guid id,
            FieldUpdate<string> label,
            FieldUpdate<string> icon,
            FieldUpdate<IReadOnlyCollection<string>> roles,
            FieldUpdate<Guid?> parentId,
            FieldUpdate<int> sortOrder,
            FieldUpdate<bool> isEnabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

    private sealed class RecordingStampRotator : IUserSecurityStampRotator
    {
        public Guid? RotatedUserId { get; private set; }
        public Guid? InvalidatedUserId { get; private set; }

        public Task RotateAsync(Guid userId, CancellationToken cancellationToken)
        {
            RotatedUserId = userId;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
        {
            InvalidatedUserId = userId;
            return Task.CompletedTask;
        }
    }

    /// <summary>抛异常型 stamp 轮换器：模拟 stamp 轮换阶段失败，验证 UoW 回滚语义。</summary>
    private sealed class ThrowingStampRotator : IUserSecurityStampRotator
    {
        public Task RotateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("stamp rotation failed");

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("stamp invalidation failed");
    }

    /// <summary>记录型 UoW：直接执行操作，同时记录执行次数与是否抛异常，供单元测试验证事务边界被调用。</summary>
    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int ExecuteCount { get; private set; }
        public bool LastOperationThrew { get; private set; }

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            try
            {
                await operation(cancellationToken);
            }
            catch
            {
                LastOperationThrew = true;
                throw;
            }
        }
    }

    /// <summary>角色管理 stub；仅覆盖 <see cref="IRoleAdmin.NameExistsAsync"/> 用于 NormalizeRoles 的存在性校验。</summary>
    private sealed class RecordingRoleAdmin : IRoleAdmin
    {
        private readonly HashSet<string> _roles = new(StringComparer.Ordinal);

        public RecordingRoleAdmin Seed(string name)
        {
            _roles.Add(name);
            return this;
        }

        public Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Contains(name));

        public Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountAssignmentsAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListAssignedUserIdsAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>IRoleRegistry stub：支持预设 Admin 持有状态与持有者计数，用于自我降级保护测试。</summary>
    private sealed class RecordingRoleRegistry : IRoleRegistry
    {
        private readonly HashSet<string> _roles = new(StringComparer.Ordinal);
        private bool _userIsAdmin;
        private int _holders;

        public RecordingRoleRegistry Seed(string name)
        {
            _roles.Add(name);
            return this;
        }

        public RecordingRoleRegistry SetUserIsAdmin(bool value)
        {
            _userIsAdmin = value;
            return this;
        }

        public RecordingRoleRegistry SetHolders(int count)
        {
            _holders = count;
            return this;
        }

        public Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Contains(name));

        public Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_userIsAdmin && string.Equals(name, "Admin", StringComparison.Ordinal));

        public Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_holders);
    }
}
