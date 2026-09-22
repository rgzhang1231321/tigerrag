using TigerRAG.Application.Documents.Indexing;

namespace TigerRAG.Infrastructure.Indexing;

/// <summary>固定窗口分块：按段落优先切分，超出窗口时按字符截断并保留重叠。</summary>
public sealed class FixedWindowChunker : ITextChunker
{
    private const int WindowSize = 500;
    private const int OverlapSize = 50;

    public IReadOnlyList<TextChunk> Split(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<TextChunk>();
        }

        var chunks = new List<TextChunk>();
        var position = 0;
        while (position < content.Length)
        {
            var remaining = content.Length - position;
            var take = Math.Min(WindowSize, remaining);
            // 尝试在窗口内找最后一个换行符，使分块边界贴近自然段落。
            if (take == WindowSize && position + take < content.Length)
            {
                var lastNewLine = content.LastIndexOf('\n', position + take - 1, WindowSize);
                if (lastNewLine > position)
                {
                    take = lastNewLine - position + 1;
                }
            }
            var segment = content.Substring(position, take);
            chunks.Add(new TextChunk(Guid.NewGuid().ToString(), segment, null));
            if (position + take >= content.Length)
            {
                break;
            }
            position += Math.Max(1, take - OverlapSize);
        }
        return chunks;
    }
}
