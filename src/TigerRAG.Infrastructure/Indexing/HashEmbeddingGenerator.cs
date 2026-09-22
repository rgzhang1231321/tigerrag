using System.Security.Cryptography;
using System.Text;
using TigerRAG.Application.Documents.Indexing;

namespace TigerRAG.Infrastructure.Indexing;

/// <summary>Hash 占位 Embedding：SHA256 扩展到 1536 维并 L2 归一化。
/// 仅用于验收流水线，不具备语义检索能力；Phase 2 替换为 OpenAI 协议实现。</summary>
public sealed class HashEmbeddingGenerator : IEmbeddingGenerator
{
    private const int Dimensions = 1536;

    public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vectors = new float[chunks.Count][];
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            vectors[i] = Embed(chunks[i].Content);
        }
        return Task.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var seedBytes = Encoding.UTF8.GetBytes(text);
        var bytesNeeded = Dimensions * sizeof(float);
        var buffer = new byte[bytesNeeded];
        // SHA256 输出 32 字节，需要 (Dimensions*4)/32 = 192 轮拼接。
        var rounds = (bytesNeeded + 31) / 32;
        for (var round = 0; round < rounds; round++)
        {
            using var sha = SHA256.Create();
            var input = new byte[seedBytes.Length + 4];
            Buffer.BlockCopy(seedBytes, 0, input, 0, seedBytes.Length);
            BitConverter.GetBytes(round).CopyTo(input, seedBytes.Length);
            var hash = sha.ComputeHash(input);
            var offset = round * 32;
            var copy = Math.Min(32, bytesNeeded - offset);
            Buffer.BlockCopy(hash, 0, buffer, offset, copy);
        }
        Buffer.BlockCopy(buffer, 0, vector, 0, bytesNeeded);
        // L2 归一化
        var sum = 0.0;
        for (var i = 0; i < Dimensions; i++)
        {
            sum += vector[i] * vector[i];
        }
        var norm = Math.Sqrt(sum);
        if (norm > 0)
        {
            for (var i = 0; i < Dimensions; i++)
            {
                vector[i] = (float)(vector[i] / norm);
            }
        }
        return vector;
    }
}
