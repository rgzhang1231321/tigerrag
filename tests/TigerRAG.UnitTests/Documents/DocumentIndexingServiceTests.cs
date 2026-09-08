using TigerRAG.Application.Documents;
using TigerRAG.Domain.Documents;

namespace TigerRAG.UnitTests.Documents;

public sealed class DocumentIndexingServiceTests
{
    [Fact]
    public async Task IndexAsync_ProcessesDocumentAndPersistsIndexedStatus()
    {
        var calls = new List<string>();
        var document = Document.Create(Guid.NewGuid(), "guide.txt", "documents/guide.txt");
        var repository = new RecordingRepository(document, calls);
        var service = new DocumentIndexingService(
            repository,
            new RecordingFileStorage(calls),
            new RecordingParser(calls),
            new RecordingChunker(calls),
            new RecordingEmbeddingGenerator(calls),
            new RecordingVectorIndex(calls));

        await service.IndexAsync(document.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Indexed, document.Status);
        Assert.Equal(1, document.ChunkCount);
        Assert.Equal(
            ["load", "save:Processing", "download", "parse", "chunk", "embed", "index", "save:Indexed"],
            calls);
    }

    private sealed class RecordingRepository(Document document, List<string> calls) : IDocumentRepository
    {
        public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            calls.Add("load");
            return Task.FromResult<Document?>(document.Id == id ? document : null);
        }

        public Task SaveAsync(Document value, CancellationToken cancellationToken)
        {
            calls.Add($"save:{value.Status}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFileStorage(List<string> calls) : IDocumentFileStorage
    {
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            calls.Add("download");
            return Task.FromResult<Stream>(new MemoryStream("content"u8.ToArray()));
        }
    }

    private sealed class RecordingParser(List<string> calls) : IDocumentParser
    {
        public Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
        {
            calls.Add("parse");
            return Task.FromResult("TigerRAG content");
        }
    }

    private sealed class RecordingChunker(List<string> calls) : ITextChunker
    {
        public IReadOnlyList<TextChunk> Split(string content)
        {
            calls.Add("chunk");
            return [new TextChunk("chunk-1", content, 1)];
        }
    }

    private sealed class RecordingEmbeddingGenerator(List<string> calls) : IEmbeddingGenerator
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(
            IReadOnlyList<TextChunk> chunks,
            CancellationToken cancellationToken)
        {
            calls.Add("embed");
            return Task.FromResult<IReadOnlyList<float[]>>([new float[] { 0.1f, 0.2f }]);
        }
    }

    private sealed class RecordingVectorIndex(List<string> calls) : IVectorIndex
    {
        public Task ReplaceDocumentAsync(
            Document document,
            IReadOnlyList<TextChunk> chunks,
            IReadOnlyList<float[]> vectors,
            CancellationToken cancellationToken)
        {
            calls.Add("index");
            return Task.CompletedTask;
        }
    }
}
