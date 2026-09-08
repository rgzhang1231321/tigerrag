using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

public sealed class DocumentAccessServiceTests
{
    [Fact]
    public async Task GetScopeAsync_ForAdmin_GrantsAllDocumentsWithoutDalQuery()
    {
        var dal = new RecordingDocumentAccessDal([]);
        var service = new DocumentAccessService(dal);

        var scope = await service.GetScopeAsync(
            Guid.NewGuid(),
            [SystemRoles.Admin],
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
        var service = new DocumentAccessService(dal);
        var userId = Guid.NewGuid();

        var scope = await service.GetScopeAsync(
            userId,
            [SystemRoles.Viewer],
            CancellationToken.None);

        Assert.False(scope.AllDocuments);
        Assert.Equal([allowedDocument], scope.DocumentIds);
        Assert.Equal(userId, dal.UserId);
        Assert.Equal([SystemRoles.Viewer], dal.Roles);
    }

    [Fact]
    public async Task ReplacePermissionsAsync_WithUnknownRole_RejectsRequest()
    {
        var dal = new RecordingDocumentAccessDal([]);
        var service = new DocumentAccessService(dal);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.ReplacePermissionsAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            isAdmin: false,
            [],
            ["SuperUser"],
            CancellationToken.None));

        Assert.Contains("SuperUser", error.Message);
        Assert.Null(dal.PermissionDocumentId);
    }

    [Fact]
    public async Task ReplacePermissionsAsync_WithKnownPrincipals_NormalizesAndUsesDal()
    {
        var dal = new RecordingDocumentAccessDal([]);
        var service = new DocumentAccessService(dal);
        var actorId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.ReplacePermissionsAsync(
            documentId,
            actorId,
            isAdmin: true,
            [userId, userId],
            [SystemRoles.Viewer, SystemRoles.Editor, SystemRoles.Viewer],
            CancellationToken.None);

        Assert.Equal(documentId, dal.PermissionDocumentId);
        Assert.Equal(actorId, dal.ActorId);
        Assert.True(dal.IsAdmin);
        Assert.Equal([userId], dal.PermissionUserIds);
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], dal.PermissionRoles);
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
