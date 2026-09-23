using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace TigerRAG.UnitTests.Documents;

public sealed class DocumentIndexingServiceTests
{
    [Fact]
    public async Task IndexAsync_ProcessesDocumentAndPersistsIndexedStatus()
    {
        var calls = new List<string>();
        var document = Document.Create(Guid.NewGuid(), "guide.txt", "documents/guide.txt");
        document.StartProcessing();
        var repository = new RecordingRepository(document, calls);
        var audit = new RecordingAuditWriter(calls);
        var service = new DocumentIndexingService(
            repository,
            new RecordingLifecycleDal(),
            new RecordingFileStorage(calls),
            new RecordingParser(calls),
            new RecordingChunker(calls),
            new RecordingEmbeddingGenerator(calls),
            new RecordingVectorIndex(calls),
            new FakeUnitOfWork(),
            audit,
            new StubLogger());

        await service.IndexAsync(document.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Indexed, document.Status);
        Assert.Equal(1, document.ChunkCount);
        Assert.Equal(
            ["load", "audit:start", "download", "parse", "chunk", "embed", "index", "load", "save:Indexed", "audit:success"],
            calls);
    }

    [Fact]
    public async Task IndexAsync_ParserFailure_MarksFailedAndAudits()
    {
        var calls = new List<string>();
        var document = Document.Create(Guid.NewGuid(), "broken.txt", "documents/broken.txt");
        document.StartProcessing();
        var repository = new RecordingRepository(document, calls);
        var audit = new RecordingAuditWriter(calls);
        var service = new DocumentIndexingService(
            repository,
            new RecordingLifecycleDal(),
            new RecordingFileStorage(calls),
            new FailingParser("parse boom"),
            new RecordingChunker(calls),
            new RecordingEmbeddingGenerator(calls),
            new RecordingVectorIndex(calls),
            new FakeUnitOfWork(),
            audit,
            new StubLogger());

        await service.IndexAsync(document.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("parse boom", document.FailureReason);
        Assert.Contains("audit:failed", calls);
        Assert.Contains(OperationAuditActions.DocumentIndexFailed, audit.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task IndexAsync_VectorFailure_MarksFailedAndAudits()
    {
        var calls = new List<string>();
        var document = Document.Create(Guid.NewGuid(), "broken.txt", "documents/broken.txt");
        document.StartProcessing();
        var repository = new RecordingRepository(document, calls);
        var audit = new RecordingAuditWriter(calls);
        var service = new DocumentIndexingService(
            repository,
            new RecordingLifecycleDal(),
            new RecordingFileStorage(calls),
            new RecordingParser(calls),
            new RecordingChunker(calls),
            new RecordingEmbeddingGenerator(calls),
            new FailingVectorIndex("vector boom"),
            new FakeUnitOfWork(),
            audit,
            new StubLogger());

        await service.IndexAsync(document.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("vector boom", document.FailureReason);
        Assert.Contains(OperationAuditActions.DocumentIndexFailed, audit.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task IndexAsync_DocumentNotProcessing_Throws()
    {
        var calls = new List<string>();
        var document = Document.Create(Guid.NewGuid(), "guide.txt", "documents/guide.txt");
        var repository = new RecordingRepository(document, calls);
        var service = new DocumentIndexingService(
            repository,
            new RecordingLifecycleDal(),
            new RecordingFileStorage(calls),
            new RecordingParser(calls),
            new RecordingChunker(calls),
            new RecordingEmbeddingGenerator(calls),
            new RecordingVectorIndex(calls),
            new FakeUnitOfWork(),
            new RecordingAuditWriter(calls),
            new StubLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.IndexAsync(document.Id, CancellationToken.None));
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

        public Task<bool> WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class RecordingParser(List<string> calls) : IDocumentParser
    {
        public Task<string> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken)
        {
            calls.Add("parse");
            return Task.FromResult("TigerRAG content");
        }
    }

    private sealed class FailingParser(string message) : IDocumentParser
    {
        public Task<string> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);
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

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FailingVectorIndex(string message) : IVectorIndex
    {
        public Task ReplaceDocumentAsync(Document document, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingLifecycleDal : IDocumentLifecycleDal
    {
        public Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingAuditWriter(List<string> calls) : IOperationAuditWriter
    {
        public readonly List<OperationAuditEntry> Entries = [];

        public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
        {
            var stage = entry.Action switch
            {
                OperationAuditActions.DocumentIndexStart => "start",
                OperationAuditActions.DocumentIndexSuccess => "success",
                OperationAuditActions.DocumentIndexFailed => "failed",
                _ => entry.Action,
            };
            calls.Add($"audit:{stage}");
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class StubLogger : ILogger<DocumentIndexingService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}