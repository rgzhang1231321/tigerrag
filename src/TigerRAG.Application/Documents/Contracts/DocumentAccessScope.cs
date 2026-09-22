namespace TigerRAG.Application.Documents;

/// <summary>用户可访问的文档范围：<c>AllDocuments</c>=true 时忽略 <c>DocumentIds</c>。</summary>
public sealed record DocumentAccessScope(bool AllDocuments, IReadOnlyList<Guid> DocumentIds);