using TigerRAG.Application.Roles;

namespace TigerRAG.UnitTests.Roles;

/// <summary>角色名格式校验单元测试。</summary>
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
}
