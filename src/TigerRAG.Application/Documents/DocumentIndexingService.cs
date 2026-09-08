namespace TigerRAG.Application.Documents;

public sealed class DocumentIndexingService(
    IDocumentRepository documents,
    IDocumentFileStorage fileStorage,
    IDocumentParser parser,
    ITextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    IVectorIndex vectorIndex)
{
    public async Task IndexAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documents.FindAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} was not found.");

        document.StartProcessing();
        await documents.SaveAsync(document, cancellationToken);

        await using var file = await fileStorage.OpenReadAsync(document.StoragePath, cancellationToken);
        var content = await parser.ParseAsync(file, cancellationToken);
        var chunks = chunker.Split(content);
        var vectors = await embeddingGenerator.GenerateAsync(chunks, cancellationToken);

        await vectorIndex.ReplaceDocumentAsync(document, chunks, vectors, cancellationToken);

        document.CompleteIndexing(chunks.Count);
        await documents.SaveAsync(document, cancellationToken);
    }
}
