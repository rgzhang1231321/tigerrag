using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

public sealed class RolePermissionMapTests
{
    [Theory]
    [InlineData(SystemRoles.Admin, SystemPermissions.ManageUsers, true)]
    [InlineData(SystemRoles.Admin, SystemPermissions.ReadAudit, true)]
    [InlineData(SystemRoles.KbManager, SystemPermissions.ManageKnowledgeBases, true)]
    [InlineData(SystemRoles.KbManager, SystemPermissions.ManageUsers, false)]
    [InlineData(SystemRoles.Editor, SystemPermissions.ManageDocuments, true)]
    [InlineData(SystemRoles.Editor, SystemPermissions.ManageKnowledgeBases, false)]
    [InlineData(SystemRoles.Viewer, SystemPermissions.UseChat, true)]
    [InlineData(SystemRoles.Viewer, SystemPermissions.ManageDocuments, false)]
    [InlineData(SystemRoles.Auditor, SystemPermissions.ReadAudit, true)]
    [InlineData(SystemRoles.Auditor, SystemPermissions.UseChat, false)]
    public void IsAllowed_UsesFixedPhaseOneRoleMatrix(string role, string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissionMap.IsAllowed(role, permission));
    }
}
