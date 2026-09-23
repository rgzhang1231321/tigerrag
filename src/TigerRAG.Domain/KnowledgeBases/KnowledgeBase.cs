namespace TigerRAG.Domain.KnowledgeBases;

/// <summary>知识库聚合根。所有不变量由领域方法集中维护，外部不能直接修改字段。</summary>
public sealed class KnowledgeBase
{
    private KnowledgeBase(string name, string? description, Guid ownerId, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    private KnowledgeBase(string name, string? description, Guid ownerId, DateTimeOffset createdAt, Guid id)
    {
        Id = id;
        Name = name;
        Description = description;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>工厂方法：创建一个知识库。名称 1..200 字，必填；描述可选。</summary>
    public static KnowledgeBase Create(string name, string? description, Guid ownerId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("名称不能为空。", nameof(name));
        }
        if (name.Length > 200)
        {
            throw new ArgumentException("名称长度不能超过 200。", nameof(name));
        }
        return new KnowledgeBase(name, description, ownerId, createdAt);
    }

    /// <summary>从持久化重建；跳过业务不变量校验（数据已过校验入库）。</summary>
    internal static KnowledgeBase Reconstitute(
        Guid id,
        string name,
        string? description,
        Guid ownerId,
        DateTimeOffset createdAt)
    {
        return new KnowledgeBase(name, description, ownerId, createdAt, id);
    }

    /// <summary>重命名；要求新名称非空且不超过 200 字，且与当前名称不同。</summary>
    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("名称不能为空。", nameof(newName));
        }
        if (newName.Length > 200)
        {
            throw new ArgumentException("名称长度不能超过 200。", nameof(newName));
        }
        if (newName == Name)
        {
            return;
        }
        Name = newName;
    }

    /// <summary>更新描述；传 null 表示清空，调用方区分"未传"与"清空"用 Nullable.HasValue。</summary>
    public void UpdateDescription(string? description)
    {
        Description = description;
    }

    /// <summary>转移 Owner 给新用户；新 Owner 必须与当前 Owner 不同。</summary>
    public void TransferOwnership(Guid newOwnerId)
    {
        if (newOwnerId == OwnerId)
        {
            throw new InvalidOperationException("新 Owner 不能与当前 Owner 相同。");
        }
        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("新 Owner 不能为空。", nameof(newOwnerId));
        }
        OwnerId = newOwnerId;
    }
}
