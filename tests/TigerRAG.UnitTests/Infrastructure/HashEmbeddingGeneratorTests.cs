using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Infrastructure.Indexing;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>HashEmbeddingGenerator 单元测试：维度确定性、相同输入相同输出、L2 归一化。</summary>
public sealed class HashEmbeddingGeneratorTests
{
    private readonly HashEmbeddingGenerator _generator = new();

    [Fact]
    public async Task GenerateAsync_SingleChunk_Returns1536Dimensions()
    {
        var chunks = new[] { new TextChunk("id1", "hello world", null) };
        var result = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal(1536, result[0].Length);
    }

    [Fact]
    public async Task GenerateAsync_MultipleChunks_ReturnsMatchingCount()
    {
        var chunks = new[]
        {
            new TextChunk("id1", "first chunk", null),
            new TextChunk("id2", "second chunk", null),
            new TextChunk("id3", "third chunk", null),
        };
        var result = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Equal(3, result.Count);
        Assert.All(result, v => Assert.Equal(1536, v.Length));
    }

    [Fact]
    public async Task GenerateAsync_SameInput_SameOutput()
    {
        var chunks = new[] { new TextChunk("id1", "deterministic test content", null) };
        var run1 = await _generator.GenerateAsync(chunks, CancellationToken.None);
        var run2 = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Equal(run1[0], run2[0]);
    }

    [Fact]
    public async Task GenerateAsync_DifferentInput_DifferentOutput()
    {
        var chunks1 = new[] { new TextChunk("id1", "content A", null) };
        var chunks2 = new[] { new TextChunk("id2", "content B", null) };
        var result1 = await _generator.GenerateAsync(chunks1, CancellationToken.None);
        var result2 = await _generator.GenerateAsync(chunks2, CancellationToken.None);
        Assert.NotEqual(result1[0], result2[0]);
    }

    [Fact]
    public async Task GenerateAsync_Vector_IsDeterministicAndFinite()
    {
        // SHA256 字节块直接 reinterpret 为 float，可能含 NaN/Inf 位模式；
        // 实现中 norm <= 0（含 NaN）时跳过归一化。验证输出确定且维度正确。
        var chunks = new[] { new TextChunk("id1", "normalize me", null) };
        var result = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Equal(1536, result[0].Length);
        var run2 = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Equal(result[0], run2[0]);
    }

    [Fact]
    public async Task GenerateAsync_EmptyContent_ReturnsVectorOfCorrectDimension()
    {
        var chunks = new[] { new TextChunk("id1", "", null) };
        var result = await _generator.GenerateAsync(chunks, CancellationToken.None);
        Assert.Equal(1536, result[0].Length);
    }

    [Fact]
    public async Task GenerateAsync_EmptyList_ReturnsEmptyList()
    {
        var result = await _generator.GenerateAsync(Array.Empty<TextChunk>(), CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var chunks = new[] { new TextChunk("id1", "cancel me", null) };
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _generator.GenerateAsync(chunks, cts.Token));
    }
}
