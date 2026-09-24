namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>批量删除知识库请求体。</summary>
public sealed record BatchDeleteKnowledgeBasesRequest(IReadOnlyList<Guid> Ids);
