using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.Roles;

namespace TigerRAG.UnitTests.KnowledgeBases;

/// <summary>KnowledgeBaseAccessService 行为测试：覆盖 Admin 短路、普通用户并集、ACL 替换归一化、权限查询授权。</summary>
public sealed class KnowledgeBaseAccessServiceTests
{
    private static readonly Guid AdminUser = Guid.Parse("8f86fa4c-c8e5-4bc0-a563-528b70a74576");

    [Fact]
    public async Task GetScopeAsync_ForAdmin_GrantsAllKnowledgeBasesWithoutDalQuery()
    {
        var dal = new RecordingKbAccessDal([]);
        var registry = new FakeRoleRegistry(true);
        var service = new KnowledgeBaseAccessService(dal, registry);

        var scope = await service.GetScopeAsync(AdminUser, ["Admin"], CancellationToken.None);

        Assert.True(scope.AllKnowledgeBase);
        Assert.Empty(scope.KbIds);
        Assert.False(dal.WasCalled);
    }

    [Fact]
    public async Task GetScopeAsync_ForOrdinaryUser_UsesUserAndRoleAcl()
    {
        var allowedKb = Guid.NewGuid();
        var dal = new RecordingKbAccessDal([allowedKb]);
        var registry = new FakeRoleRegistry(false);
        var service = new KnowledgeBaseAccessService(dal, registry);
        var userId = Guid.NewGuid();

        var scope = await service.GetScopeAsync(userId, ["Viewer"], CancellationToken.None);

        Assert.False(scope.AllKnowledgeBase);
        Assert.Equal([allowedKb], scope.KbIds);
        Assert.Equal(userId, dal.UserId);
        Assert.Equal(["Viewer"], dal.Roles);
    }

    [Fact]
    public async Task ReplacePermissionsAsync_WithKnownPrincipals_NormalizesAndUsesDal()
    {
        var dal = new RecordingKbAccessDal([]);
        var registry = new FakeRoleRegistry(false);
        var service = new KnowledgeBaseAccessService(dal, registry);
        var actorId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.ReplacePermissionsAsync(
            kbId,
            actorId,
            isAdmin: true,
            [userId, userId],
            ["Viewer", "Editor", "Viewer"],
            CancellationToken.None);

        Assert.Equal(kbId, dal.PermissionKbId);
        Assert.Equal(actorId, dal.ActorId);
        Assert.True(dal.IsAdmin);
        Assert.Equal([userId], dal.PermissionUserIds);
        Assert.Equal(["Editor", "Viewer"], dal.PermissionRoles);
    }

    [Fact]
    public async Task GetPermissionsAsync_ForAdmin_ReturnsCurrentAcl()
    {
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var dal = new RecordingKbAccessDal([])
        {
            OwnerId = userId,
            Snapshot = new KbPermissionsSnapshot([userId], [roleId])
        };
        var registry = new FakeRoleRegistry(true) { RoleNames = ["Viewer"] };
        var service = new KnowledgeBaseAccessService(dal, registry);

        var permissions = await service.GetPermissionsAsync(kbId, Guid.NewGuid(), isAdmin: true, CancellationToken.None);

        Assert.Equal([userId], permissions.UserIds);
        Assert.Equal(["Viewer"], permissions.Roles);
    }

    [Fact]
    public async Task GetPermissionsAsync_ForNonOwner_Forbidden()
    {
        var dal = new RecordingKbAccessDal([]) { OwnerId = Guid.NewGuid() };
        var registry = new FakeRoleRegistry(false);
        var service = new KnowledgeBaseAccessService(dal, registry);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetPermissionsAsync(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetPermissionsAsync_ForMissingKb_ThrowsKeyNotFound()
    {
        var dal = new RecordingKbAccessDal([]);
        var registry = new FakeRoleRegistry(true);
        var service = new KnowledgeBaseAccessService(dal, registry);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetPermissionsAsync(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true, CancellationToken.None));
    }

    /// <summary>测试用 IRoleRegistry 伪造：直接返回预设的 Admin 持有状态，不走 DB。</summary>
    private sealed class FakeRoleRegistry(bool isAdmin) : IRoleRegistry
    {
        public IReadOnlyCollection<string> RoleNames { get; init; } = [];

        public Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(isAdmin && userId == AdminUser && string.Equals(name, "Admin", StringComparison.Ordinal));

        public Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(isAdmin ? 1 : 0);

        public Task<IReadOnlyCollection<string>> GetRoleNamesAsync(
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(RoleNames);
    }

    /// <summary>测试用 IKbAccessDal 伪造：记录调用参数，返回预设结果。</summary>
    private sealed class RecordingKbAccessDal(IReadOnlyList<Guid> result) : IKbAccessDal
    {
        public bool WasCalled { get; private set; }
        public Guid? UserId { get; private set; }
        public IReadOnlyCollection<string>? Roles { get; private set; }
        public Guid? PermissionKbId { get; private set; }
        public Guid? ActorId { get; private set; }
        public bool IsAdmin { get; private set; }
        public IReadOnlyCollection<Guid>? PermissionUserIds { get; private set; }
        public IReadOnlyCollection<string>? PermissionRoles { get; private set; }

        public Guid? OwnerId { get; init; }
        public KbPermissionsSnapshot? Snapshot { get; init; }

        public Task<IReadOnlyList<Guid>> GetAccessibleKbIdsAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            Roles = roles;
            return Task.FromResult(result);
        }

        public Task<KbPermissionsSnapshot> GetPermissionsAsync(
            Guid kbId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot ?? new KbPermissionsSnapshot([], []));

        public Task<Guid?> GetKbOwnerIdAsync(
            Guid kbId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OwnerId);

        public Task ReplacePermissionsAsync(
            Guid kbId,
            Guid actorId,
            bool isAdmin,
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            PermissionKbId = kbId;
            ActorId = actorId;
            IsAdmin = isAdmin;
            PermissionUserIds = userIds;
            PermissionRoles = roles;
            return Task.CompletedTask;
        }
    }
}
