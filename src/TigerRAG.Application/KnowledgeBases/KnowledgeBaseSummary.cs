namespace TigerRAG.Application.KnowledgeBases;

/// <summary>知识库列表单项（含 OwnerName 由 Service 端组装）。</summary>
public sealed record KnowledgeBaseSummary(
    Guid Id,
    string Name,
    string? Description,
    Guid OwnerId,
    DateTimeOffset CreatedAt);