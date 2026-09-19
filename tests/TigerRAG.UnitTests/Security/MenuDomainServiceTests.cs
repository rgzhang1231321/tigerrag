using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

public sealed class MenuDomainServiceTests
{
    private static MenuConfigItem NewItem(Guid id, Guid? parentId, string[]? roles = null) =>
        new(id, "key", "Label", null, roles ?? Array.Empty<string>(), parentId, 0, true);

    [Fact]
    public static void ValidateParent_NullParent_IsAllowed()
    {
        var items = new[] { NewItem(Guid.NewGuid(), null) };
        MenuDomainService.ValidateParent(items[0].Id, null, items);
    }

    [Fact]
    public static void ValidateParent_ExistingParent_IsAllowed()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var items = new[]
        {
            NewItem(parentId, null),
            NewItem(childId, parentId),
        };
        MenuDomainService.ValidateParent(childId, parentId, items);
    }

    [Fact]
    public static void ValidateParent_SelfAsParent_Throws()
    {
        var id = Guid.NewGuid();
        var items = new[] { NewItem(id, null) };
        var error = Assert.Throws<ArgumentException>(() => MenuDomainService.ValidateParent(id, id, items));
        Assert.Contains("自身", error.Message);
    }

    [Fact]
    public static void ValidateParent_NonExistentParent_Throws()
    {
        var id = Guid.NewGuid();
        var items = new[] { NewItem(id, null) };
        var error = Assert.Throws<ArgumentException>(() => MenuDomainService.ValidateParent(id, Guid.NewGuid(), items));
        Assert.Contains("不存在", error.Message);
    }

    [Fact]
    public static void ValidateParent_CircularAncestor_Throws()
    {
        // 树：Root → A → B。若把 Root 的父设为 B，则 B 是 Root 的后代，构成环。
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new[]
        {
            NewItem(root, null),
            NewItem(a, root),
            NewItem(b, a),
        };
        var error = Assert.Throws<ArgumentException>(() => MenuDomainService.ValidateParent(root, b, items));
        Assert.Contains("循环", error.Message);
    }

    [Fact]
    public static void ValidateParent_DeepCycle_Throws()
    {
        // 树：A → B → C → D。若把 A 的父设为 D，则 D 是 A 的后代，构成环。
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var items = new[]
        {
            NewItem(a, null),
            NewItem(b, a),
            NewItem(c, b),
            NewItem(d, c),
        };
        var error = Assert.Throws<ArgumentException>(() => MenuDomainService.ValidateParent(a, d, items));
        Assert.Contains("循环", error.Message);
    }

    /// <summary>验证"把节点移到其兄弟节点下"是合法操作（不构成循环）。</summary>
    [Fact]
    public static void ValidateParent_MoveToSiblingSubtree_IsAllowed()
    {
        // 树：Root → A、Root → B。把 A 的父设为 B —— A 无后代，不构成环。
        var root = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new[]
        {
            NewItem(root, null),
            NewItem(a, root),
            NewItem(b, root),
        };
        MenuDomainService.ValidateParent(a, b, items);
    }

    [Fact]
    public static void ValidateRoles_Null_IsAllowed()
    {
        MenuDomainService.ValidateRoles(null);
    }

    [Fact]
    public static void ValidateRoles_Empty_IsAllowed()
    {
        MenuDomainService.ValidateRoles(Array.Empty<string>());
    }

    [Fact]
    public static void ValidateRoles_NonEmptyDistinct_IsAllowed()
    {
        MenuDomainService.ValidateRoles(new[] { SystemRoles.Admin, SystemRoles.Viewer });
    }

    [Fact]
    public static void ValidateRoles_BlankRole_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            MenuDomainService.ValidateRoles(new[] { SystemRoles.Admin, " " }));
        Assert.Contains("角色名不能为空", error.Message);
    }

    [Fact]
    public static void ValidateRoles_DuplicateRole_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            MenuDomainService.ValidateRoles(new[] { SystemRoles.Admin, SystemRoles.Admin }));
        Assert.Contains("重复", error.Message);
    }
}
