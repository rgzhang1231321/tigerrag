using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>创建知识库请求体。</summary>
public sealed record CreateKnowledgeBaseRequest(
    [Required] string Name,
    string? Description);