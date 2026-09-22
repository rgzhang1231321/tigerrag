using TigerRAG.Domain.KnowledgeBases;

namespace TigerRAG.UnitTests.KnowledgeBases;

public sealed class KnowledgeBaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Create_SetsNameDescriptionOwnerAndCreatedAt()
    {
        var ownerId = Guid.NewGuid();
        var kb = KnowledgeBase.Create("产品手册", "v1 版本", ownerId, Now);

        Assert.Equal("产品手册", kb.Name);
        Assert.Equal("v1 版本", kb.Description);
        Assert.Equal(ownerId, kb.OwnerId);
        Assert.Equal(Now, kb.CreatedAt);
        Assert.NotEqual(Guid.Empty, kb.Id);
    }

    [Fact]
    public void Create_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeBase.Create("", null, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => KnowledgeBase.Create("   ", null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        var longName = new string('a', 201);
        Assert.Throws<ArgumentException>(() => KnowledgeBase.Create(longName, null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Rename_ToValidName_UpdatesName()
    {
        var kb = KnowledgeBase.Create("产品手册", null, Guid.NewGuid(), Now);

        kb.Rename("产品手册 v2");

        Assert.Equal("产品手册 v2", kb.Name);
    }

    [Fact]
    public void Rename_ToEmpty_Throws()
    {
        var kb = KnowledgeBase.Create("产品手册", null, Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() => kb.Rename(""));
    }

    [Fact]
    public void Rename_ToSameName_IsNoOp()
    {
        var kb = KnowledgeBase.Create("产品手册", null, Guid.NewGuid(), Now);

        kb.Rename("产品手册");

        Assert.Equal("产品手册", kb.Name);
    }

    [Fact]
    public void UpdateDescription_ToNull_ClearsDescription()
    {
        var kb = KnowledgeBase.Create("产品手册", "v1 版本", Guid.NewGuid(), Now);

        kb.UpdateDescription(null);

        Assert.Null(kb.Description);
    }

    [Fact]
    public void UpdateDescription_ToNewValue_UpdatesDescription()
    {
        var kb = KnowledgeBase.Create("产品手册", "v1 版本", Guid.NewGuid(), Now);

        kb.UpdateDescription("v2 版本");

        Assert.Equal("v2 版本", kb.Description);
    }

    [Fact]
    public void TransferOwnership_ToOtherUser_UpdatesOwner()
    {
        var originalOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var kb = KnowledgeBase.Create("产品手册", null, originalOwner, Now);

        kb.TransferOwnership(newOwner);

        Assert.Equal(newOwner, kb.OwnerId);
    }

    [Fact]
    public void TransferOwnership_ToSameOwner_Throws()
    {
        var owner = Guid.NewGuid();
        var kb = KnowledgeBase.Create("产品手册", null, owner, Now);

        Assert.Throws<InvalidOperationException>(() => kb.TransferOwnership(owner));
    }

    [Fact]
    public void TransferOwnership_ToEmpty_Throws()
    {
        var kb = KnowledgeBase.Create("产品手册", null, Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() => kb.TransferOwnership(Guid.Empty));
    }
}
