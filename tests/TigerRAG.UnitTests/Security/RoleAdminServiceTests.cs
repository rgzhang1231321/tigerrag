using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

/// <summary>角色管理服务单元测试：List/Create/Delete 的保留集、存在性、事务与 stamp 轮换语义。</summary>
public sealed class RoleAdminServiceTests
{
    [Fact]
    public async Task ListAsync_MarksSystemRolesAndSortsAlphabetically()
    {
        var roleAdmin = new RecordingRoleAdmin()
            .Seed("Viewer")
            .Seed("CustomRole")
            .Seed("Admin");
        var service = BuildService(roleAdmin);

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Equal(
            [
                new RoleDto("Admin", true),
                new RoleDto("CustomRole", false),
                new RoleDto("Viewer", true),
            ],
            result);
    }

    [Fact]
    public async Task ListAsync_Empty_ReturnsEmptyList()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateAsync_WithValidCustomName_CreatesSuccessfully()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var result = await service.CreateRoleAsync(new CreateRoleRequest("CustomRole"), CancellationToken.None);

        Assert.Equal(new RoleDto("CustomRole", false), result);
        Assert.Equal(["CustomRole"], roleAdmin.CreatedNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("aAdmin")]
    [InlineData("Custom_Role")]
    [InlineData("Custom.Role")]
    public async Task CreateAsync_WithInvalidFormat_ThrowsArgumentException(string name)
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest(name), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WithReservedName_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest(SystemRoles.Admin), CancellationToken.None));

        Assert.Contains("系统角色", error.Message);
        Assert.Empty(roleAdmin.CreatedNames);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest("CustomRole"), CancellationToken.None));

        Assert.Contains("已存在", error.Message);
        Assert.Empty(roleAdmin.CreatedNames);
    }

    [Theory]
    [InlineData(SystemRoles.Admin)]
    [InlineData(SystemRoles.KbManager)]
    public async Task DeleteAsync_WithReservedName_ThrowsArgumentException(string name)
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync(name, CancellationToken.None));

        Assert.Contains("不可删除", error.Message);
        Assert.False(roleAdmin.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_WhenRoleMissing_ReturnsFalse()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var deleted = await service.DeleteAsync("CustomRole", CancellationToken.None);

        Assert.False(deleted);
        Assert.False(roleAdmin.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_WithAssignedUsers_StillDeletesAndRotatesStamp()
    {
        // 角色被用户持有时，删除仍允许：受影响用户必须 stamp 轮换 + refresh 撤销，
        // 否则其旧 JWT 的 role claim 与 AspNetUserRoles 实际状态不一致。
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var userIds = new[] { Guid.NewGuid() };
        roleAdmin.SetAssignedUsers("CustomRole", userIds);
        var rotator = new RecordingStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, rotator: rotator, sessions: sessions);

        var deleted = await service.DeleteAsync("CustomRole", CancellationToken.None);

        Assert.True(deleted);
        Assert.True(roleAdmin.DeleteCalled);
        Assert.Equal(userIds, rotator.RotatedUserIds);
        Assert.Equal(userIds, sessions.RevokedUserIds);
    }

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesRoleMappingsAndRoleAndRotatesStampAndRevokesSessions()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        roleAdmin.SetAssignedUsers("CustomRole", userIds);
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = BuildService(roleAdmin, rotator: rotator, sessions: sessions);

        var deleted = await service.DeleteAsync("CustomRole", CancellationToken.None);

        Assert.True(deleted);
        Assert.True(roleAdmin.DeleteCalled);
        Assert.Equal(userIds.OrderBy(g => g), rotator.RotatedUserIds.OrderBy(g => g));
        Assert.Equal(userIds.OrderBy(g => g), sessions.RevokedUserIds.OrderBy(g => g));
    }

    [Fact]
    public async Task DeleteAsync_WhenStampRotationFails_InvalidatesRotatedUsersAndRethrows()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        roleAdmin.SetAssignedUsers("CustomRole", userIds);
        // 仅在第二次 RotateAsync 抛异常，模拟部分用户已完成 stamp 轮换。
        var rotator = new FailingAfterFirstStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, rotator: rotator, sessions: sessions);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync("CustomRole", CancellationToken.None));

        // 只有第一个用户的 stamp 实际写入了 Redis，必须清掉；其余用户在轮换前失败无需清。
        Assert.Equal([userIds[0]], rotator.InvalidatedUserIds);
    }

    private static RoleAdminService BuildService(
        IRoleAdmin roleAdmin,
        IUserSecurityStampRotator? rotator = null,
        IRefreshSessionDal? sessions = null,
        IUnitOfWork? unitOfWork = null) =>
        new(roleAdmin, unitOfWork ?? new RecordingUnitOfWork(), rotator ?? new RecordingStampRotator(), sessions ?? new RecordingRefreshSessionDal());

    // ----- recording stubs -----

    private sealed class RecordingRoleAdmin : IRoleAdmin
    {
        private readonly Dictionary<string, RoleRecord> _roles = new(StringComparer.Ordinal);

        public List<string> CreatedNames { get; } = [];
        public bool DeleteCalled { get; private set; }

        public RecordingRoleAdmin Seed(string name) =>
            Seed(name, isSystem: false);

        public RecordingRoleAdmin Seed(string name, bool isSystem)
        {
            _roles[name] = new RoleRecord(name, isSystem);
            return this;
        }

        public void SetAssignmentCount(string name, int count)
        {
            if (!_roles.TryGetValue(name, out var record))
            {
                record = new RoleRecord(name, false);
                _roles[name] = record;
            }
            record.AssignmentCount = count;
        }


        public void SetAssignedUsers(string name, IReadOnlyList<Guid> userIds)
        {
            if (!_roles.TryGetValue(name, out var record))
            {
                record = new RoleRecord(name, false);
                _roles[name] = record;
            }
            record.AssignedUsers = userIds;
            record.AssignmentCount = userIds.Count;
        }

        public Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleDto>>(
                _roles.Values
                    .Select(record => new RoleDto(record.Name, record.IsSystem))
                    .ToArray());

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.ContainsKey(name));

        public Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken)
        {
            CreatedNames.Add(name);
            _roles[name] = new RoleRecord(name, false);
            return Task.FromResult(new RoleDto(name, false));
        }

        public Task<int> CountAssignmentsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.TryGetValue(name, out var record) ? record.AssignmentCount : 0);

        public Task<IReadOnlyList<Guid>> ListAssignedUserIdsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(
                _roles.TryGetValue(name, out var record)
                    ? (IReadOnlyList<Guid>)record.AssignedUsers.ToArray()
                    : Array.Empty<Guid>());


        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.FromResult(_roles.Remove(name));
        }
        }

        private sealed class RoleRecord
        {
            public RoleRecord(string name, bool isSystem)
            {
                Name = name;
                IsSystem = isSystem;
            }

            public string Name { get; }
            public bool IsSystem { get; }
            public int AssignmentCount { get; set; }
            public IReadOnlyList<Guid> AssignedUsers { get; set; } = Array.Empty<Guid>();
        }

    private sealed class RecordingRefreshSessionDal : IRefreshSessionDal
    {
        public List<Guid> RevokedUserIds { get; } = [];

        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
        {
            RevokedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStampRotator : IUserSecurityStampRotator
    {
        public List<Guid> RotatedUserIds { get; } = [];
        public List<Guid> InvalidatedUserIds { get; } = [];

        public Task RotateAsync(Guid userId, CancellationToken cancellationToken)
        {
            RotatedUserIds.Add(userId);
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
        {
            InvalidatedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAfterFirstStampRotator : IUserSecurityStampRotator
    {
        private int _rotateCalls;
        public List<Guid> InvalidatedUserIds { get; } = [];

        public Task RotateAsync(Guid userId, CancellationToken cancellationToken)
        {
            _rotateCalls++;
            if (_rotateCalls == 1)
            {
                return Task.CompletedTask;
            }

            throw new InvalidOperationException("stamp rotation failed");
        }

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
        {
            InvalidatedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            await operation(cancellationToken);
        }
    }
}
