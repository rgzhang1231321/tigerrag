namespace TigerRAG.Api.Controllers.Documents;

/// <summary>批量删除文档请求体。</summary>
public sealed record BatchDeleteDocumentsRequest(IReadOnlyList<Guid> Ids);
