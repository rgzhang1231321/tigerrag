using TigerRAG.Application.Documents;
using TigerRAG.Application.Roles;

namespace TigerRAG.UnitTests.Documents;

public sealed class DocumentAccessServiceTests
{
    [Fact]
    public async Task GetScopeAsync_ForAdmin_GrantsAllDocumentsWithoutDalQuery()
    {
        var dal = new RecordingDocumentAccessDal([]);
        var registry = new FakeRoleRegistry(true); // 用户持有 Admin
        var service = new DocumentAccessService(dal, registry);

        var scope = await service.GetScopeAsync(
            Guid.NewGuid(),
            ["Admin"],
            CancellationToken.None);

        Assert.True(scope.AllDocuments);
        Assert.Empty(scope.DocumentIds);
        Assert.False(dal.WasCalled);
    }

    [Fact]
    public async Task GetScopeAsync_ForOrdinaryUser_UsesUserAndRoleAcl()
    {
        var allowedDocument = Guid.NewGuid();
        var dal = new RecordingDocumentAccessDal([allowedDocument]);
        var registry = new FakeRoleRegistry(false); // 用户不持有 Admin
        var service = new DocumentAccessService(dal, registry);
        var userId = Guid.NewGuid();

        var scope = await service.GetScopeAsync(
            userId,
            ["Viewer"],
            CancellationToken.None);

        Assert.False(scope.AllDocuments);
        Assert.Equal([allowedDocument], scope.DocumentIds);
        Assert.Equal(userId, dal.UserId);
        Assert.Equal(["Viewer"], dal.Roles);
    }

    [Fact]
    public async Task ReplacePermissionsAsync_WithKnownPrincipals_NormalizesAndUsesDal()
    {
        var dal = new RecordingDocumentAccessDal([]);
        var registry = new FakeRoleRegistry(false);
        var service = new DocumentAccessService(dal, registry);
        var actorId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.ReplacePermissionsAsync(
            documentId,
            actorId,
            isAdmin: true,
            [userId, userId],
            ["Viewer", "Editor", "Viewer"],
            CancellationToken.None);

        Assert.Equal(documentId, dal.PermissionDocumentId);
        Assert.Equal(actorId, dal.ActorId);
        Assert.True(dal.IsAdmin);
        Assert.Equal([userId], dal.PermissionUserIds);
        Assert.Equal(["Editor", "Viewer"], dal.PermissionRoles);
    }

    /// <summary>测试用 IRoleRegistry 伪造：直接返回预设的 Admin 持有状态，不走 DB。</summary>
    private sealed class FakeRoleRegistry(bool isAdmin) : IRoleRegistry
    {
        public Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(isAdmin && string.Equals(name, "Admin", StringComparison.Ordinal));

        public Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(isAdmin ? 1 : 0);
    }

    private sealed class RecordingDocumentAccessDal(IReadOnlyList<Guid> result) : IDocumentAccessDal
    {
        public bool WasCalled { get; private set; }
        public Guid? UserId { get; private set; }
        public IReadOnlyCollection<string>? Roles { get; private set; }
        public Guid? PermissionDocumentId { get; private set; }
        public Guid? ActorId { get; private set; }
        public bool IsAdmin { get; private set; }
        public IReadOnlyCollection<Guid>? PermissionUserIds { get; private set; }
        public IReadOnlyCollection<string>? PermissionRoles { get; private set; }

        public Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            Roles = roles;
            return Task.FromResult(result);
        }

        public Task ReplacePermissionsAsync(
            Guid documentId,
            Guid actorId,
            bool isAdmin,
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            PermissionDocumentId = documentId;
            ActorId = actorId;
            IsAdmin = isAdmin;
            PermissionUserIds = userIds;
            PermissionRoles = roles;
            return Task.CompletedTask;
        }
    }
}
