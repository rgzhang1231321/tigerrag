using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Indexing.Interface;
using DomainDocument = TigerRAG.Domain.Documents.Document;

namespace TigerRAG.Infrastructure.Indexing;

/// <summary>Qdrant 向量索引实现。按 documentId 先清后写保证幂等。</summary>
public sealed class QdrantVectorIndex(
    QdrantClient client,
    IOptions<VectorIndexOptions> options) : IVectorIndex
{
    private readonly VectorIndexOptions _options = options.Value;
    private readonly uint _dimensions = (uint)options.Value.Dimensions;

    /// <summary>替换指定文档的向量索引。按 documentId 先清后写保证幂等。</summary>
    /// <param name="document">待索引的领域文档。</param>
    /// <param name="chunks">文档拆分后的文本块列表。</param>
    /// <param name="vectors">与文本块一一对应的向量列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ReplaceDocumentAsync(
        DomainDocument document,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 根据策略解析 collection 名；Shared 下所有 KB 共享同一 collection。
        var collection = ResolveCollectionName(document);

        // 确保 collection 存在；幂等。
        await EnsureCollectionAsync(collection, cancellationToken);

        // 先清后写：按 documentId 过滤条件删除旧点。
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "document_id",
                        Match = new Match { Keyword = document.Id.ToString() },
                    },
                },
            },
        };
        await client.DeleteAsync(collection, filter, cancellationToken: cancellationToken);

        // 写入新点。
        if (chunks.Count == 0)
        {
            return;
        }
        var points = new List<PointStruct>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            points.Add(new PointStruct
            {
                Id = new PointId { Uuid = chunks[i].Id },
                Vectors = vectors[i],
                Payload =
                {
                    ["document_id"] = document.Id.ToString(),
                    ["kb_id"] = document.KnowledgeBaseId.ToString(),
                    ["position"] = i,
                    ["content"] = chunks[i].Content,
                },
            });
        }
        await client.UpsertAsync(collection, points, cancellationToken: cancellationToken);
    }

    /// <summary>删除指定文档的全部向量点；文档不存在视为成功（filter 不命中不抛错）。</summary>
    /// <param name="documentId">文档 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "document_id",
                        Match = new Match { Keyword = documentId.ToString() },
                    },
                },
            },
        };

        // Shared 策略：所有 KB 在同一 collection，单 documentId 精确删除。
        // PerKnowledgeBase 策略：当前实现下仅删 Shared collection；若后续启用多 KB 时再遍历全部 collection。
        if (_options.Strategy == VectorCollectionStrategy.Shared)
        {
            await client.DeleteAsync(_options.Collection, filter, cancellationToken: cancellationToken);
        }
    }

    /// <summary>根据集合策略解析目标 collection 名。Shared 策略共享同一 collection，PerKnowledgeBase 按 KB ID 模板生成。</summary>
    /// <param name="document">领域文档，用于获取 KnowledgeBaseId。</param>
    /// <returns>目标 collection 名称。</returns>
    private string ResolveCollectionName(DomainDocument document) => _options.Strategy switch
    {
        VectorCollectionStrategy.Shared => _options.Collection,
        VectorCollectionStrategy.PerKnowledgeBase
            => string.Format(_options.CollectionNameTemplate, document.KnowledgeBaseId),
        _ => throw new InvalidOperationException(
            $"Unknown vector collection strategy: {_options.Strategy}"),
    };

    /// <summary>确保目标 collection 存在。若不存在则使用 Cosine 距离和指定维度创建。</summary>
    /// <param name="collection">collection 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task EnsureCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        var exists = await client.CollectionExistsAsync(collection, cancellationToken);
        if (exists)
        {
            return;
        }
        await client.CreateCollectionAsync(
            collection,
            new VectorParams
            {
                Size = _dimensions,
                Distance = Distance.Cosine,
            },
            cancellationToken: cancellationToken);
    }
}
