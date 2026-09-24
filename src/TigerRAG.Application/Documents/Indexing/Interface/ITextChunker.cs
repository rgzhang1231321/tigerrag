namespace TigerRAG.Application.Documents.Indexing.Interface;

/// <summary>文本分块端口；典型策略为 500 token 窗口 + 50 token 重叠。</summary>
public interface ITextChunker
{
    IReadOnlyList<TextChunk> Split(string content);
}