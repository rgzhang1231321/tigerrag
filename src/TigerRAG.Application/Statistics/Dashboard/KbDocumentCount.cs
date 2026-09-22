namespace TigerRAG.Application.Statistics.Dashboard;

/// <summary>知识库文档数分布。</summary>
public sealed record KbDocumentCount(Guid KnowledgeBaseId, string KnowledgeBaseName, int DocumentCount);