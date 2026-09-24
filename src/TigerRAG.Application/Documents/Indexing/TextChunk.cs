namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>索引流水线用的最小文本片段。</summary>
public sealed record TextChunk(string Id, string Content, int? PageNumber);
