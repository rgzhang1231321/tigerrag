using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.IntegrationTests.Infrastructure;

public sealed class DalBehaviorTests
{
    [Fact]
    public void ApplyPermissionChanges_WhenExistingPermissionRemains_DoesNotTrackDuplicateKey()
    {
        using var context = CreateContext();
        var documentId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var existing = new DocumentPermissionRecord
        {
            DocumentId = documentId,
            PrincipalType = PermissionPrincipalType.User,
            PrincipalId = existingUserId
        };
        context.Attach(existing);

        DocumentAccessDal.ApplyPermissionChanges(
            context,
            documentId,
            [existing],
            [existingUserId],
            [newRoleId]);

        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.Single(context.ChangeTracker.Entries<DocumentPermissionRecord>(),
            entry => entry.State == EntityState.Added && entry.Entity.PrincipalId == newRoleId);
    }

    [Fact]
    public void ApplyRoleChanges_WhenExistingRoleRemains_DoesNotTrackDuplicateKey()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var existingRoleId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var existing = new IdentityUserRole<Guid> { UserId = userId, RoleId = existingRoleId };
        context.Attach(existing);

        UserDal.ApplyRoleChanges(context, userId, [existing], [existingRoleId, newRoleId]);

        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.Single(context.ChangeTracker.Entries<IdentityUserRole<Guid>>(),
            entry => entry.State == EntityState.Added && entry.Entity.RoleId == newRoleId);
    }

    private static TigerRagDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TigerRagDbContext>()
            .UseNpgsql("Host=localhost;Database=change_tracker_test")
            .Options;
        return new TigerRagDbContext(options);
    }
}
