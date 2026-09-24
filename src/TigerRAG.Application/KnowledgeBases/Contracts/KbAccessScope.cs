namespace TigerRAG.Application.KnowledgeBases;

/// <summary>用户可访问的 KB 范围：AllKnowledgeBase=true 时忽略 KbIds。</summary>
public sealed record KbAccessScope(bool AllKnowledgeBase, IReadOnlyList<Guid> KbIds);
