namespace TigerRAG.Application.Documents;

/// <summary>文档索引编排服务。流水线：下载 → 解析 → 分块 → Embedding → 写入向量库 → 更新状态。</summary>
public sealed class DocumentIndexingService(
    IDocumentRepository documents,
    IDocumentFileStorage fileStorage,
    IDocumentParser parser,
    ITextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    IVectorIndex vectorIndex)
{
    /// <summary>处理一条消息：状态由 Pending 经 Processing 最终落到 Indexed；异常时落到 Failed。</summary>
    public async Task IndexAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documents.FindAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} was not found.");

        // 进入 Processing；任何后续异常都会被 Document 状态机标记为 Failed。
        document.StartProcessing();
        await documents.SaveAsync(document, cancellationToken);

        await using var file = await fileStorage.OpenReadAsync(document.StoragePath, cancellationToken);
        var content = await parser.ParseAsync(file, null, cancellationToken);
        var chunks = chunker.Split(content);
        var vectors = await embeddingGenerator.GenerateAsync(chunks, cancellationToken);

        // 先清后写：相同 documentId 多次处理结果一致（向量库侧幂等）。
        await vectorIndex.ReplaceDocumentAsync(document, chunks, vectors, cancellationToken);

        document.CompleteIndexing(chunks.Count);
        await documents.SaveAsync(document, cancellationToken);
    }
}
