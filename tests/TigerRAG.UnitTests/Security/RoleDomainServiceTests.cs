using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

/// <summary>角色名格式与保留集判定单元测试。</summary>
public sealed class RoleDomainServiceTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("KbManager")]
    [InlineData("CustomRole")]
    [InlineData("A1")]
    public void EnsureName_WithValidName_DoesNotThrow(string name) =>
        RoleDomainService.EnsureName(name);

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("aAdmin")]
    [InlineData("Admin_Test")]
    [InlineData("Admin.Test")]
    [InlineData("Admin Test")]
    [InlineData("Admin*")]
    public void EnsureName_WithInvalidName_ThrowsArgumentException(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => RoleDomainService.EnsureName(name));
        Assert.Contains("角色名", error.Message);
    }

    [Fact]
    public void EnsureName_WithTooLongName_ThrowsArgumentException()
    {
        var name = new string('A', 65);
        var error = Assert.Throws<ArgumentException>(() => RoleDomainService.EnsureName(name));
        Assert.Contains("角色名", error.Message);
    }

    [Theory]
    [InlineData(SystemRoles.Admin)]
    [InlineData(SystemRoles.KbManager)]
    [InlineData(SystemRoles.Editor)]
    [InlineData(SystemRoles.Viewer)]
    [InlineData(SystemRoles.Auditor)]
    public void IsReserved_WithSystemRole_ReturnsTrue(string name) =>
        Assert.True(RoleDomainService.IsReserved(name));

    [Fact]
    public void IsReserved_WithCustomRole_ReturnsFalse() =>
        Assert.False(RoleDomainService.IsReserved("CustomRole"));
}
