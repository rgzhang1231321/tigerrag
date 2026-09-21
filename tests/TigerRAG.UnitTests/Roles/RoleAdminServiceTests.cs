using TigerRAG.Application.Auth;
using TigerRAG.Application.Menus;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.UnitTests.Roles;

/// <summary>角色管理服务单元测试：List/Create/Delete 的引用计数、Admin 保护、级联清理与事务回滚语义。</summary>
public sealed class RoleAdminServiceTests
{
    [Fact]
    public async Task ListAsync_MarksAdminAsSystemAndSortsAlphabetically()
    {
        var roleAdmin = new RecordingRoleAdmin()
            .Seed("Viewer")
            .Seed("CustomRole")
            .Seed("Admin");
        var service = BuildService(roleAdmin);

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Collection(result,
            item => Assert.Equal(new RoleDto("Admin", true, 0, 0, []), item),
            item => Assert.Equal(new RoleDto("CustomRole", false, 0, 0, []), item),
            item => Assert.Equal(new RoleDto("Viewer", false, 0, 0, []), item));
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
    public async Task ListAsync_AttachesUserCountAndMenuNames()
    {
        var roleAdmin = new RecordingRoleAdmin()
            .Seed("Viewer")
            .Seed("KbManager");
        roleAdmin.SetAssignmentCount("Viewer", 3);
        roleAdmin.SetAssignmentCount("KbManager", 0);
        var menuReference = new RecordingMenuReference()
            .WithMenuNames("Viewer", "问答工作台", "E2E 验证页")
            .WithMenuNames("KbManager", "知识库");
        var menus = new[]
        {
            new MenuConfigItem(Guid.NewGuid(), "/chat", "问答工作台", null, ["Admin", "KbManager", "Editor", "Viewer"], null, 40, true),
            new MenuConfigItem(Guid.NewGuid(), "/e2e-test", "E2E 验证页", null, ["Admin", "Viewer"], null, 70, true),
            new MenuConfigItem(Guid.NewGuid(), "/knowledge-bases", "知识库", null, ["Admin", "KbManager"], null, 20, true),
        };
        var service = BuildService(roleAdmin, menuReference, menus: menus);

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("KbManager", item.Name);
                Assert.Equal(0, item.UserCount);
                Assert.Equal(2, item.MenuCount);
                Assert.Equal(["知识库", "问答工作台"], item.MenuNames);
            },
            item =>
            {
                Assert.Equal("Viewer", item.Name);
                Assert.Equal(3, item.UserCount);
                Assert.Equal(2, item.MenuCount);
                Assert.Equal(["E2E 验证页", "问答工作台"], item.MenuNames);
            });
    }

    [Fact]
    public async Task CreateAsync_WithValidCustomName_CreatesSuccessfully()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var result = await service.CreateRoleAsync(new CreateRoleRequest("CustomRole"), CancellationToken.None);

        Assert.Equal(new RoleDto("CustomRole", false, 0, 0, []), result);
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
    public async Task CreateAsync_WithAdminName_StillCreates()
    {
        // Admin 是受保护不可删，但可重建（运行期同步）。
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var result = await service.CreateRoleAsync(new CreateRoleRequest("Admin"), CancellationToken.None);

        Assert.Equal("Admin", result.Name);
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

    [Fact]
    public async Task DeleteAsync_WithAdminName_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("Admin");
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync("Admin", CancellationToken.None));

        Assert.Contains("不可删除", error.Message);
        Assert.False(roleAdmin.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_NonAdminWithReservedName_DeletesSuccessfully()
    {
        // KbManager/Editor/Viewer/Auditor 原本是 "系统保留"，现在可被删除（业务按用户回答确定）。
        var roleAdmin = new RecordingRoleAdmin().Seed("Viewer");
        var menuReference = new RecordingMenuReference();
        var service = BuildService(roleAdmin, menuReference);

        var deleted = await service.DeleteAsync("Viewer", CancellationToken.None);

        Assert.True(deleted);
        Assert.True(roleAdmin.DeleteCalled);
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
    public async Task DeleteAsync_WithAssignedUsers_ThrowsArgumentException()
    {
        // 业务规则：被用户持有时拒绝删除，需先解除用户角色绑定。
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        roleAdmin.SetAssignmentCount("CustomRole", 2);
        var menuReference = new RecordingMenuReference();
        var service = BuildService(roleAdmin, menuReference);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync("CustomRole", CancellationToken.None));

        Assert.Contains("用户", error.Message);
        Assert.False(roleAdmin.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_WithMenuReferences_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var menuReference = new RecordingMenuReference()
            .WithMenuNames("CustomRole", "问答工作台", "知识库");
        var service = BuildService(roleAdmin, menuReference);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync("CustomRole", CancellationToken.None));

        Assert.Contains("菜单", error.Message);
        Assert.Contains("问答工作台", error.Message);
        Assert.False(roleAdmin.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_HappyPath_CleansMenuRefsAndDeletesRole()
    {
        // count==0 且无菜单引用，进入事务：先级联清理菜单 Roles，再删 AspNetRoles。
        // 业务中"count==0"就意味着无受影响用户，stamp 轮换与 refresh 撤销不会被调用。
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        roleAdmin.SetAssignmentCount("CustomRole", 0);
        var menuReference = new RecordingMenuReference();
        var sessions = new RecordingRefreshSessionDal();
        var rotator = new RecordingStampRotator();
        var service = BuildService(roleAdmin, menuReference, rotator, sessions);

        var deleted = await service.DeleteAsync("CustomRole", CancellationToken.None);

        Assert.True(deleted);
        Assert.True(menuReference.CleanupCalled);
        Assert.Equal("CustomRole", menuReference.LastCleanupRole);
        Assert.True(roleAdmin.DeleteCalled);
        Assert.Empty(rotator.RotatedUserIds);
        Assert.Empty(sessions.RevokedUserIds);
    }

    [Fact]
    public async Task DeleteAsync_WhenStampRotationFails_InvalidatesRotatedUsersAndRethrows()
    {
        // count==0 通过闸，但事务内 ListAssignedUserIds 仍然查出受影响用户（TOCTOU 防御）。
        // 当 stamp 轮换在第二位失败时：第一位用户已在 Redis 写入新 stamp，必须清掉；第二位起未
        // 完成轮换的不会留下新 stamp，无需清。
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        roleAdmin.SetAssignmentCount("CustomRole", 0);
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        roleAdmin.SetAssignedUsers("CustomRole", userIds);
        var rotator = new FailingAfterFirstStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, new RecordingMenuReference(), rotator, sessions);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync("CustomRole", CancellationToken.None));

        Assert.Equal([userIds[0]], rotator.InvalidatedUserIds);
    }

    [Fact]
    public async Task RenameAsync_WithAdminName_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("Admin");
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameAsync(AdminActor, "Admin", new RenameRoleRequest("SuperAdmin"), CancellationToken.None));

        Assert.Contains("不可重命名", error.Message);
        Assert.True(await roleAdmin.NameExistsAsync("Admin", CancellationToken.None));
    }

    [Fact]
    public async Task RenameAsync_WithSameName_IsIdempotent()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        roleAdmin.SetAssignmentCount("CustomRole", 2);
        var menuReference = new RecordingMenuReference()
            .WithMenuNames("CustomRole", "问答工作台");
        var menus = new[]
        {
            new MenuConfigItem(Guid.NewGuid(), "/chat", "问答工作台", null, ["Admin", "CustomRole"], null, 40, true),
        };
        var service = BuildService(roleAdmin, menuReference, menus: menus);

        var result = await service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("CustomRole"), CancellationToken.None);

        Assert.Equal("CustomRole", result.Name);
        Assert.Equal(2, result.UserCount);
        Assert.Equal(1, result.MenuCount);
        Assert.Equal(["问答工作台"], result.MenuNames);
    }

    [Fact]
    public async Task RenameAsync_WithInvalidNewName_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var service = BuildService(roleAdmin);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("bad-role"), CancellationToken.None));
    }

    [Fact]
    public async Task RenameAsync_WhenOldRoleMissing_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin();
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameAsync(AdminActor, "Ghost", new RenameRoleRequest("Whatever"), CancellationToken.None));

        Assert.Contains("不存在", error.Message);
    }

    [Fact]
    public async Task RenameAsync_WhenNewNameTaken_ThrowsArgumentException()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole").Seed("OtherRole");
        var service = BuildService(roleAdmin);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("OtherRole"), CancellationToken.None));

        Assert.Contains("已存在", error.Message);
    }

    [Fact]
    public async Task RenameAsync_HappyPath_RenamesAndRotatesAffectedUsers()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        roleAdmin.SetAssignedUsers("CustomRole", userIds);
        roleAdmin.SetAssignmentCount("CustomRole", userIds.Length);
        var rotator = new RecordingStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, new RecordingMenuReference(), rotator, sessions);

        var result = await service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("RenamedRole"), CancellationToken.None);

        Assert.Equal("RenamedRole", result.Name);
        Assert.False(await roleAdmin.NameExistsAsync("CustomRole", CancellationToken.None));
        Assert.True(await roleAdmin.NameExistsAsync("RenamedRole", CancellationToken.None));
        Assert.Equal(userIds.OrderBy(g => g), rotator.RotatedUserIds.OrderBy(g => g));
        Assert.Equal(userIds.OrderBy(g => g), sessions.RevokedUserIds.OrderBy(g => g));
    }

    [Fact]
    public async Task RenameAsync_WithMenuReferences_RecordsMenuReferencesUpdateAudit()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var menuReference = new RecordingMenuReference()
            .WithMenuNames("CustomRole", "问答工作台", "知识库");
        var rotator = new RecordingStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, menuReference, rotator, sessions);

        await service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("RenamedRole"), CancellationToken.None);

        Assert.True(menuReference.RenameCalled);
        Assert.Equal("CustomRole", menuReference.RenameOldName);
        Assert.Equal("RenamedRole", menuReference.RenameNewName);
    }

    [Fact]
    public async Task RenameAsync_WhenNoMenuReferences_SkipsMenuReferencesUpdateAudit()
    {
        var roleAdmin = new RecordingRoleAdmin().Seed("CustomRole");
        var menuReference = new RecordingMenuReference();
        var rotator = new RecordingStampRotator();
        var sessions = new RecordingRefreshSessionDal();
        var service = BuildService(roleAdmin, menuReference, rotator, sessions);

        await service.RenameAsync(AdminActor, "CustomRole", new RenameRoleRequest("RenamedRole"), CancellationToken.None);
    }

    private static readonly ActorContext AdminActor = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "admin");

    private static RoleAdminService BuildService(
        IRoleAdmin roleAdmin,
        IRoleMenuReference? menuReference = null,
        IUserSecurityStampRotator? rotator = null,
        IRefreshSessionDal? sessions = null,
        IUnitOfWork? unitOfWork = null,
        IReadOnlyList<MenuConfigItem>? menus = null) =>
        new(roleAdmin,
            menuReference ?? new RecordingMenuReference(),
            new RecordingMenuConfigDal(menus ?? []),
            unitOfWork ?? new RecordingUnitOfWork(),
            rotator ?? new RecordingStampRotator(),
            sessions ?? new RecordingRefreshSessionDal(),
            new NoopAuditWriter());

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
            // 注意：不联动 AssignmentCount——业务中 count==0 才会进入事务；但事务内仍会查
            // ListAssignedUserIdsAsync 以防御 count 与列表查询之间的并发插入 TOCTOU 窗口。
            if (!_roles.TryGetValue(name, out var record))
            {
                record = new RoleRecord(name, false);
                _roles[name] = record;
            }
            record.AssignedUsers = userIds;
        }

        public Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleDto>>(
                _roles.Values
                    .Select(record => new RoleDto(record.Name, record.IsSystem, 0, 0, []))
                    .ToArray());

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.ContainsKey(name));

        public Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken)
        {
            CreatedNames.Add(name);
            _roles[name] = new RoleRecord(name, false);
            return Task.FromResult(new RoleDto(name, false, 0, 0, []));
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

        public Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken)
        {
            if (!_roles.TryGetValue(oldName, out var record))
            {
                return Task.FromResult(false);
            }

            _roles.Remove(oldName);
            _roles[newName] = new RoleRecord(newName, record.IsSystem)
            {
                AssignmentCount = record.AssignmentCount,
                AssignedUsers = record.AssignedUsers,
            };
            return Task.FromResult(true);
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
    }

    private sealed class RecordingMenuReference : IRoleMenuReference
    {
        private readonly Dictionary<string, List<string>> _names = new(StringComparer.Ordinal);

        public bool CleanupCalled { get; private set; }
        public string? LastCleanupRole { get; private set; }
        public bool RenameCalled { get; private set; }
        public string? RenameOldName { get; private set; }
        public string? RenameNewName { get; private set; }

        public RecordingMenuReference WithMenuNames(string role, params string[] labels)
        {
            _names[role] = labels.ToList();
            return this;
        }

        public Task<IReadOnlyList<string>> ListMenuNamesAsync(string roleName, CancellationToken cancellationToken) =>
            Task.FromResult(
                (IReadOnlyList<string>)(_names.TryGetValue(roleName, out var labels)
                    ? labels.OrderBy(l => l, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>()));

        public Task<int> RemoveRoleFromAllMenusAsync(string roleName, CancellationToken cancellationToken)
        {
            CleanupCalled = true;
            LastCleanupRole = roleName;
            _names.Remove(roleName);
            return Task.FromResult(0);
        }

        public Task<int> RenameRoleInAllMenusAsync(string oldName, string newName, CancellationToken cancellationToken)
        {
            RenameCalled = true;
            RenameOldName = oldName;
            RenameNewName = newName;

            var affected = 0;
            foreach (var key in _names.Keys.ToList())
            {
                if (_names[key].Contains(oldName, StringComparer.Ordinal))
                {
                    _names[key] = _names[key]
                        .Select(r => string.Equals(r, oldName, StringComparison.Ordinal) ? newName : r)
                        .ToList();
                    affected++;
                }
            }
            return Task.FromResult(affected);
        }
    }

    private sealed class NoopAuditWriter : IOperationAuditWriter
    {
        public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingMenuConfigDal : IMenuConfigDal
    {
        private readonly IReadOnlyList<MenuConfigItem> _items;
        public RecordingMenuConfigDal(IReadOnlyList<MenuConfigItem> items) => _items = items;

        public Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_items);

        public Task<MenuConfigItem> CreateAsync(string key, string label, string? icon, IReadOnlyCollection<string> roles, Guid? parentId, int sortOrder, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<MenuConfigItem?> UpdateAsync(Guid id, FieldUpdate<string> label, FieldUpdate<string> icon, FieldUpdate<IReadOnlyCollection<string>> roles, FieldUpdate<Guid?> parentId, FieldUpdate<int> sortOrder, FieldUpdate<bool> isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
