using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Queue;
using TigerRAG.Worker;

namespace TigerRAG.UnitTests.Worker;

public sealed class DocumentIndexingWorkerTests
{
    [Fact]
    public async Task Worker_DequeueAndClaimsAndIndexes()
    {
        var lifecycle = new RecordingLifecycleDal();
        var query = new RecordingQueryDal();
        var spyRepo = new RecordingSpyRepository();
        var doc = TigerRAG.Domain.Documents.Document.Create(Guid.NewGuid(), "guide.txt", "documents/guide.txt", 1024);
        spyRepo.Seed(doc);
        var docId = doc.Id;
        var queue = new ScriptedQueue([docId]);
        var indexing = new RecordingIndexingService(spyRepo);
        var worker = BuildWorker(queue, lifecycle, indexing, query: query, repository: spyRepo);

        await RunOnce(worker);

        Assert.Equal(1, lifecycle.Claimed.Count);
        Assert.Equal(docId, lifecycle.Claimed[0]);
        Assert.Equal(DocumentStatus.Indexed, spyRepo.Documents[docId].Status);
    }

    [Fact]
    public async Task Worker_DequeueNull_ContinuesLoop()
    {
        var queue = new ScriptedQueue([]);
        var lifecycle = new RecordingLifecycleDal();
        var query = new RecordingQueryDal();
        var spyRepo = new RecordingSpyRepository();
        var indexing = new RecordingIndexingService(spyRepo);
        var worker = BuildWorker(queue, lifecycle, indexing, query: query, repository: spyRepo, dequeueTimeout: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await worker.StartAsync(cts.Token);
        try
        {
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(lifecycle.Claimed);
    }

    [Fact]
    public async Task Worker_ClaimFailed_SkipsToIndexing()
    {
        var lifecycle = new RecordingLifecycleDal { ClaimSucceeds = false };
        var query = new RecordingQueryDal();
        var spyRepo = new RecordingSpyRepository();
        var doc = TigerRAG.Domain.Documents.Document.Create(Guid.NewGuid(), "guide.txt", "documents/guide.txt", 1024);
        spyRepo.Seed(doc);
        var docId = doc.Id;
        var queue = new ScriptedQueue([docId]);
        var indexing = new RecordingIndexingService(spyRepo);
        var worker = BuildWorker(queue, lifecycle, indexing, query: query, repository: spyRepo);

        await RunOnce(worker);

        Assert.Empty(lifecycle.Claimed);
        Assert.NotEqual(DocumentStatus.Indexed, spyRepo.Documents[docId].Status);
    }

    [Fact]
    public async Task Worker_SwallowsIndexingException()
    {
        // 让 Parser 抛异常 → IndexAsync 进入 Failed 路径，状态被 Domain 改为 Failed；
        // 编排异常被 Worker 吞掉，不会让循环中断。
        var docId = Guid.NewGuid();
        var queue = new ScriptedQueue([docId]);
        var lifecycle = new RecordingLifecycleDal();
        var query = new RecordingQueryDal();
        var services = new ServiceCollection();
        services.AddSingleton(queue);
        services.AddSingleton<IDocumentLifecycleDal>(lifecycle);
        services.AddSingleton<IDocumentQueryDal>(query);
        var indexing = BuildFailingIndexingService();
        // 注册基类键
        services.AddSingleton<DocumentIndexingService>(indexing);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new DocumentIndexQueueOptions
        {
            ProcessingTimeoutSeconds = 300,
            RecoveryScanIntervalSeconds = 60,
            DequeueTimeoutSeconds = 1,
        });

        var worker = new DocumentIndexingWorker(sp, queue, NullLogger<DocumentIndexingWorker>.Instance, options);
        await RunOnce(worker);
        Assert.Equal(1, lifecycle.Claimed.Count);
    }

    [Fact]
    public async Task Worker_StartupRecover_EnqueuesPendingAndTimedOutProcessing()
    {
        var pendingId = Guid.NewGuid();
        var processingId = Guid.NewGuid();
        var queue = new ScriptedQueue([]);
        var lifecycle = new RecordingLifecycleDal();
        var query = new RecordingQueryDal
        {
            PendingIds = [pendingId],
            ProcessingIds = [processingId],
        };
        var spyRepo = new RecordingSpyRepository();
        var indexing = new RecordingIndexingService(spyRepo);
        var worker = BuildWorker(queue, lifecycle, indexing, query: query, repository: spyRepo);

        await RunOnce(worker);

        Assert.Contains(pendingId, queue.EnqueuedIds);
        Assert.Contains(processingId, queue.EnqueuedIds);
        Assert.Contains(processingId, lifecycle.ResetToPending);
    }

    private static DocumentIndexingWorker BuildWorker(
        IDocumentIndexQueue queue,
        IDocumentLifecycleDal lifecycle,
        DocumentIndexingService indexing,
        IDocumentQueryDal? query = null,
        IDocumentRepository? repository = null,
        TimeSpan? dequeueTimeout = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(queue);
        services.AddSingleton<IDocumentLifecycleDal>(lifecycle);
        services.AddSingleton<IDocumentQueryDal>(query ?? new RecordingQueryDal());
        services.AddSingleton<IDocumentRepository>(repository ?? new RecordingSpyRepository());
        // 注册基类键，便于 Worker 通过 GetRequiredService<DocumentIndexingService>() 解析。
        services.AddSingleton<DocumentIndexingService>(indexing);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new DocumentIndexQueueOptions
        {
            ProcessingTimeoutSeconds = 300,
            RecoveryScanIntervalSeconds = 60,
            DequeueTimeoutSeconds = (int)(dequeueTimeout ?? TimeSpan.FromSeconds(5)).TotalSeconds,
        });

        return new DocumentIndexingWorker(sp, queue, NullLogger<DocumentIndexingWorker>.Instance, options);
    }

    private static DocumentIndexingWorker BuildWorker(
        IDocumentIndexQueue queue,
        IDocumentLifecycleDal lifecycle,
        DocumentIndexingService indexing,
        IDocumentQueryDal query)
    {
        return BuildWorker(queue, lifecycle, indexing, query, repository: null);
    }

    private static DocumentIndexingService BuildFailingIndexingService()
    {
        var repo = new RecordingSpyRepository();
        var id = Guid.NewGuid();
        repo.Seed(TigerRAG.Domain.Documents.Document.Create(id, "guide.txt", "documents/guide.txt", 1024));
        return new FailingDocumentIndexingService(repo);
    }

    private static async Task RunOnce(DocumentIndexingWorker worker, TimeSpan? runFor = null)
    {
        using var cts = new CancellationTokenSource(runFor ?? TimeSpan.FromMilliseconds(150));
        await worker.StartAsync(cts.Token);
        try
        {
            await Task.Delay(300, cts.Token);
        }
        catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);
    }
}

internal sealed class FailingDocumentIndexingService(RecordingSpyRepository repository) : DocumentIndexingService(
    repository,
    new NoopLifecycleDal(),
    new FailingFileStorage(),
    new NoopParser(),
    new NoopChunker(),
    new NoopEmbedding(),
    new NoopVectorIndex(),
    new NoopUnitOfWork(),
    new NoopAuditWriter(),
    NullLogger<DocumentIndexingService>.Instance)
{ }

internal sealed class FailingFileStorage : IDocumentFileStorage
{
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom");
    public Task WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
}

// ---- Test doubles ----

internal sealed class ScriptedQueue(IReadOnlyList<Guid> scripted) : IDocumentIndexQueue
{
    private readonly Queue<Guid> _remaining = new(scripted);
    public readonly List<Guid> EnqueuedIds = [];

    public Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        EnqueuedIds.Add(documentId);
        _remaining.Enqueue(documentId);
        return Task.CompletedTask;
    }

    public Task<Guid?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_remaining.Count > 0)
        {
            return Task.FromResult<Guid?>(_remaining.Dequeue());
        }
        // 超时返回 null；调用方继续循环。
        try { return Task.Delay(timeout, cancellationToken).ContinueWith(_ => (Guid?)null, cancellationToken); }
        catch (OperationCanceledException) { return Task.FromResult<Guid?>(null); }
    }
}

internal sealed class RecordingLifecycleDal : IDocumentLifecycleDal
{
    public readonly List<Guid> Claimed = [];
    public readonly List<Guid> ResetToPending = [];
    public bool ClaimSucceeds { get; set; } = true;

    public Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        if (ClaimSucceeds) Claimed.Add(documentId);
        return Task.FromResult(ClaimSucceeds);
    }

    public Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        ResetToPending.Add(documentId);
        return Task.FromResult(true);
    }

    public Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class RecordingQueryDal : IDocumentQueryDal
{
    public List<Guid> PendingIds { get; set; } = [];
    public List<Guid> ProcessingIds { get; set; } = [];

    public Task<DocumentSummary?> FindSummaryAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DocumentSummary?>(null);
    public Task<IReadOnlyList<DocumentSummary>> ListByKbAsync(Guid kbId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DocumentSummary>>([]);
    public Task<IReadOnlyList<Guid>> ListNonProcessingIdsByKbAsync(Guid kbId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(PendingIds);
    public Task<IReadOnlyList<Guid>> ListTimedOutProcessingIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(ProcessingIds);
}

internal interface IIndexingServiceForTest
{
    bool ThrowOnIndex { get; set; }
    List<Guid> Calls { get; }
    Task IndexAsync(Guid documentId, CancellationToken cancellationToken);
}

internal sealed class RecordingIndexingService : DocumentIndexingService
{
    public RecordingIndexingService(IDocumentRepository repository) : base(
        repository,
        new NoopLifecycleDal(),
        new NoopFileStorage(),
        new NoopParser(),
        new NoopChunker(),
        new NoopEmbedding(),
        new NoopVectorIndex(),
        new NoopUnitOfWork(),
        new NoopAuditWriter(),
        NullLogger<DocumentIndexingService>.Instance)
    { }
}

/// <summary>包装 DocumentIndexingService.IndexAsync 以记录调用；通过 IDocumentRepository 模拟 Processing 状态。</summary>
internal sealed class SpyIndexingDecorator : IDocumentRepository
{
    private readonly RecordingSpyRepository _inner;
    public RecordingIndexingService Service { get; }

    public SpyIndexingDecorator(RecordingSpyRepository inner, RecordingIndexingService service)
    {
        _inner = inner;
        Service = service;
    }

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) => _inner.FindAsync(id, cancellationToken);
    public Task SaveAsync(Document document, CancellationToken cancellationToken) => _inner.SaveAsync(document, cancellationToken);
}

internal sealed class RecordingSpyRepository : IDocumentRepository
{
    public readonly Dictionary<Guid, Document> Documents = [];
    public List<Guid> IndexCalls { get; } = [];

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = Documents.GetValueOrDefault(id);
        if (stored is null) return Task.FromResult<Document?>(null);
        // 返回一个新的 Document 实例（Reconstitute），避免业务方法直接修改字典里的引用。
        var copy = TigerRAG.Domain.Documents.Document.Reconstitute(
            stored.Id, stored.KnowledgeBaseId, stored.FileName, stored.StoragePath,
            stored.Size,
            stored.Status, stored.ChunkCount, stored.FailureReason);
        return Task.FromResult<Document?>(copy);
    }

    public Task SaveAsync(Document document, CancellationToken cancellationToken)
    {
        // 模拟 EF 跟踪：SaveAsync 用最新状态覆盖 dict 中条目。
        Documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Document Seed(Document doc)
    {
        doc.StartProcessing();
        Documents[doc.Id] = doc;
        return doc;
    }
}

// ---- Noop implementations used only to construct RecordingIndexingService ----

internal sealed class NoopDocumentRepository : IDocumentRepository
{
    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Document?>(null);
    public Task SaveAsync(Document document, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoopLifecycleDal : IDocumentLifecycleDal
{
    public Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class NoopFileStorage : IDocumentFileStorage
{
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream());
    public Task WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoopParser : IDocumentParser
{
    public Task<DocumentParseResult> ParseAsync(Stream content, string? mimeType, CancellationToken cancellationToken)
        => Task.FromResult(new DocumentParseResult(string.Empty, Array.Empty<ExtractedImage>()));
}

internal sealed class NoopChunker : ITextChunker
{
    public IReadOnlyList<TextChunk> Split(string content) => [];
}

internal sealed class NoopEmbedding : IEmbeddingGenerator
{
    public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<float[]>>([]);
}

internal sealed class NoopVectorIndex : IVectorIndex
{
    public Task ReplaceDocumentAsync(Document document, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoopUnitOfWork : IUnitOfWork
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) => operation(cancellationToken);
}

internal sealed class NoopAuditWriter : IOperationAuditWriter
{
    public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
}