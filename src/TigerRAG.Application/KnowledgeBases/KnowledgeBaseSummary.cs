namespace TigerRAG.Application.KnowledgeBases;

/// <summary>知识库列表单项；OwnerName 和 DocumentCount 由 DAL 或 Service 组装。</summary>
public sealed record KnowledgeBaseSummary(
    Guid Id,
    string Name,
    string? Description,
    Guid OwnerId,
    string? OwnerName,
    int DocumentCount,
    DateTimeOffset CreatedAt);
