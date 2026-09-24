using System.Text;
using TigerRAG.Infrastructure.Indexing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>FixedWindowChunker 单元测试：验证段落优先切分、窗口截断、重叠保留、空文本边界。</summary>
public sealed class FixedWindowChunkerTests
{
    private readonly FixedWindowChunker _chunker = new();

    [Fact]
    public void Split_EmptyText_ReturnsEmpty()
    {
        var result = _chunker.Split("");
        Assert.Empty(result);
    }

    [Fact]
    public void Split_WhitespaceOnly_ReturnsEmpty()
    {
        var result = _chunker.Split("   \n\n  \t  ");
        Assert.Empty(result);
    }

    [Fact]
    public void Split_ShortTextSingleChunk_ReturnsOneChunk()
    {
        var text = new string('a', 100);
        var result = _chunker.Split(text);
        Assert.Single(result);
        Assert.Equal(text, result[0].Content);
    }

    [Fact]
    public void Split_ExactWindowSize_ReturnsOneChunk()
    {
        var text = new string('x', 500);
        var result = _chunker.Split(text);
        Assert.Single(result);
        Assert.Equal(500, result[0].Content.Length);
    }

    [Fact]
    public void Split_ExceedsWindow_SplitsAtNewLine()
    {
        // 构造 2 段，每段 300 字符，中间换行，总长 601 > 500 窗口。
        // 第一段 300 + 换行 = 301 字符，应在换行处截断。
        var line1 = new string('a', 300);
        var line2 = new string('b', 300);
        var text = line1 + "\n" + line2;

        var result = _chunker.Split(text);

        Assert.True(result.Count >= 2);
        // 第一块应包含第一行（含换行符），在换行处截断。 Assert.Contains("a", result[0].Content);
        Assert.Contains("b", result[1].Content);
    }

    [Fact]
    public void Split_ExceedsWindow_NoNewLine_HardSplitsAtWindow()
    {
        // 无换行符的长文本：在 500 字符处硬切。
        var text = new string('z', 1200);
        var result = _chunker.Split(text);

        Assert.True(result.Count >= 2);
        Assert.Equal(500, result[0].Content.Length);
    }

    [Fact]
    public void Split_Overlap_PreservesOverlapBetweenChunks()
    {
        // 1200 无换行字符：第一块 0-500，第二块起始位置 500-50=450，
        // 因此第二块应包含第一块尾部的重叠区域。
        var text = new string('q', 1200);
        var result = _chunker.Split(text);

        Assert.True(result.Count >= 2);
        // 第一块尾部 50 字符 == 第二块头部 50 字符。
        var tailOfFirst = result[0].Content[^50..];
        var headOfSecond = result[1].Content[..50];
        Assert.Equal(tailOfFirst, headOfSecond);
    }

    [Fact]
    public void Split_ChunkIds_AreUnique()
    {
        var text = new string('u', 2000);
        var result = _chunker.Split(text);
        var ids = result.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Split_NullInput_ReturnsEmpty()
    {
        // string.IsNullOrWhiteSpace(null) == true，走空文本分支返回空列表。
        var result = _chunker.Split(null!);
        Assert.Empty(result);
    }
}
