using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Domain.Documents;
using TigerRAG.Domain.KnowledgeBases;
using Microsoft.Extensions.Logging;

namespace TigerRAG.UnitTests.KnowledgeBases;

public sealed class KnowledgeBaseServiceTests
{
    private static readonly ActorContext Admin = new(Guid.Parse("8f86fa4c-c8e5-4bc0-a563-528b70a74576"), "admin");
    private static readonly ActorContext Owner = new(Guid.NewGuid(), "owner");
    private static readonly ActorContext Stranger = new(Guid.NewGuid(), "stranger");

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_PersistsAndAudits()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var docs = new FakeDocumentLifecycleDal();
        var service = CreateService(dal, audit, uow, docs);

        var result = await service.CreateAsync("Test KB", "desc", Admin, CancellationToken.None);

        Assert.Equal("Test KB", result.Name);
        Assert.Equal("desc", result.Description);
        Assert.Equal(Admin.Id, result.OwnerId);
        Assert.Single(dal.Entities);
        Assert.Equal(OperationAuditActions.KbCreate, audit.Entries[0].Action);
    }

    [Fact]
    public async Task ListAsync_AdminSeesAll()
    {
        var dal = new InMemoryKbDal();
        dal.Seed(KnowledgeBase.Create("KB1", null, Owner.Id, Now), KnowledgeBase.Create("KB2", null, Stranger.Id, Now));
        var service = CreateService(dal);

        var result = await service.ListAsync(Admin, isAdmin: true, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListAsync_NonAdminSeesOnlyOwned()
    {
        var dal = new InMemoryKbDal();
        dal.Seed(KnowledgeBase.Create("KB1", null, Owner.Id, Now), KnowledgeBase.Create("KB2", null, Stranger.Id, Now));
        var service = CreateService(dal);

        var result = await service.ListAsync(Owner, isAdmin: false, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("KB1", result[0].Name);
    }

    [Fact]
    public async Task GetAsync_NonOwner_ThrowsUnauthorized()
    {
        var dal = new InMemoryKbDal();
        var kb = KnowledgeBase.Create("KB1", null, Owner.Id, Now);
        dal.Seed(kb);
        var service = CreateService(dal);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAsync(kb.Id, Stranger, isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_Rename_UpdatesAndAudits()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var kb = KnowledgeBase.Create("Old", null, Owner.Id, Now);
        dal.Seed(kb);
        var service = CreateService(dal, audit, uow);

        var result = await service.UpdateAsync(kb.Id, new UpdateKnowledgeBaseRequest("New", null, null), Owner, isAdmin: false, CancellationToken.None);

        Assert.Equal("New", result.Name);
        Assert.Equal(OperationAuditActions.KbUpdate, audit.Entries[0].Action);
    }

    [Fact]
    public async Task UpdateAsync_NoChange_NoAudit()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var kb = KnowledgeBase.Create("Same", null, Owner.Id, Now);
        dal.Seed(kb);
        var service = CreateService(dal, audit, uow);

        var result = await service.UpdateAsync(kb.Id, new UpdateKnowledgeBaseRequest("Same", null, null), Owner, isAdmin: false, CancellationToken.None);

        Assert.Equal("Same", result.Name);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionNull_Clears()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var kb = KnowledgeBase.Create("KB", "old desc", Owner.Id, Now);
        dal.Seed(kb);
        var service = CreateService(dal, audit, uow);

        var result = await service.UpdateAsync(kb.Id, new UpdateKnowledgeBaseRequest(null, null, null), Owner, isAdmin: false, CancellationToken.None);

        Assert.Null(result.Description);
    }

    [Fact]
    public async Task DeleteAsync_Cascades()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var docs = new FakeDocumentLifecycleDal();
        var query = new FakeDocumentQueryDal();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        dal.Seed(kb);
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        dal.DocumentIds[kb.Id] = [doc1, doc2];
        query.Documents[doc1] = new DocumentSummary(doc1, kb.Id, "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        query.Documents[doc2] = new DocumentSummary(doc2, kb.Id, "b.txt", null, "p2", DocumentStatus.Failed, 0, "err", Owner.Id, Now, Now);
        var service = CreateService(dal, audit, uow, docs, queryDal: query);

        await service.DeleteAsync(kb.Id, Owner, isAdmin: false, CancellationToken.None);

        Assert.False(dal.Entities.ContainsKey(kb.Id));
        Assert.Equal(2, docs.DeletedDocumentIds.Count);
        Assert.Equal(OperationAuditActions.KbDelete, audit.Entries[0].Action);
    }

    [Fact]
    public async Task DeleteAsync_ProcessingDocument_Rejects()
    {
        var dal = new InMemoryKbDal();
        var uow = new FakeUnitOfWork();
        var docs = new FakeDocumentLifecycleDal();
        var query = new FakeDocumentQueryDal();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        dal.Seed(kb);
        var docId = Guid.NewGuid();
        dal.DocumentIds[kb.Id] = [docId];
        query.Documents[docId] = new DocumentSummary(docId, kb.Id, "test.txt", null, "path", DocumentStatus.Processing, 0, null, Owner.Id, Now, Now);
        var service = CreateService(dal, audit: new RecordingAuditWriter(), uow, docs, queryDal: query);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(kb.Id, Owner, isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ThrowsUnauthorized()
    {
        var dal = new InMemoryKbDal();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        dal.Seed(kb);
        var service = CreateService(dal);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.DeleteAsync(kb.Id, Stranger, isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task ReindexAsync_ResetsIndexedAndFailedToPending()
    {
        var dal = new InMemoryKbDal();
        var audit = new RecordingAuditWriter();
        var uow = new FakeUnitOfWork();
        var docs = new FakeDocumentLifecycleDal();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        dal.Seed(kb);
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        dal.DocumentIds[kb.Id] = [doc1, doc2];
        dal.NonProcessingIds[kb.Id] = [doc1, doc2];
        docs.Documents[doc1] = new DocumentSummary(doc1, kb.Id, "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        docs.Documents[doc2] = new DocumentSummary(doc2, kb.Id, "b.txt", null, "p2", DocumentStatus.Failed, 0, "err", Owner.Id, Now, Now);
        var queue = new RecordingQueue();
        var service = CreateService(dal, audit, uow, docs, queue);

        await service.ReindexAsync(kb.Id, Owner, isAdmin: false, CancellationToken.None);

        Assert.Equal(OperationAuditActions.KbReindex, audit.Entries[0].Action);
        Assert.Equal(2, dal.ResetCount);
        Assert.Equal(2, queue.EnqueuedIds.Count);
    }

    private static KnowledgeBaseService CreateService(
        InMemoryKbDal dal,
        RecordingAuditWriter? audit = null,
        FakeUnitOfWork? uow = null,
        FakeDocumentLifecycleDal? docs = null,
        RecordingQueue? queue = null,
        FakeDocumentQueryDal? queryDal = null)
    {
        return new KnowledgeBaseService(
            dal,
            docs ?? new FakeDocumentLifecycleDal(),
            queryDal ?? new FakeDocumentQueryDal(),
            queue ?? new RecordingQueue(),
            new FakeFileStorage(),
            new FakeVectorIndex(),
            uow ?? new FakeUnitOfWork(),
            audit ?? new RecordingAuditWriter(),
            new FakeUserLookup(),
            new StubLogger());
    }
}

// ---- Fakes ----

internal sealed class InMemoryKbDal : IKbDal
{
    public readonly Dictionary<Guid, KnowledgeBase> Entities = [];
    public readonly Dictionary<Guid, List<Guid>> DocumentIds = [];
    public readonly Dictionary<Guid, List<Guid>> NonProcessingIds = [];
    public int ResetCount;

    public void Seed(params KnowledgeBase[] kbs)
    {
        foreach (var kb in kbs) Entities[kb.Id] = kb;
    }

    public Task<KnowledgeBase?> FindAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Entities.GetValueOrDefault(id));

    public Task<IReadOnlyList<KnowledgeBaseSummary>> ListAsync(Guid actorId, bool isAdmin, CancellationToken cancellationToken)
    {
        var query = Entities.Values.AsEnumerable();
        if (!isAdmin) query = query.Where(kb => kb.OwnerId == actorId);
        return Task.FromResult<IReadOnlyList<KnowledgeBaseSummary>>(query
            .Select(kb => new KnowledgeBaseSummary(kb.Id, kb.Name, kb.Description, kb.OwnerId, null, 0, kb.CreatedAt))
            .ToList());
    }

    public Task InsertAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        Entities[knowledgeBase.Id] = knowledgeBase;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        Entities[knowledgeBase.Id] = knowledgeBase;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(DocumentIds.GetValueOrDefault(kbId) ?? []);

    public Task DeleteAsync(Guid kbId, CancellationToken cancellationToken)
    {
        Entities.Remove(kbId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListNonProcessingDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(NonProcessingIds.GetValueOrDefault(kbId) ?? []);

    public Task<int> ResetNonProcessingToPendingAsync(Guid kbId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        ResetCount = NonProcessingIds.GetValueOrDefault(kbId)?.Count ?? 0;
        return Task.FromResult(ResetCount);
    }
}

internal sealed class FakeDocumentLifecycleDal : IDocumentLifecycleDal
{
    public readonly Dictionary<Guid, DocumentSummary> Documents = [];
    public readonly List<Guid> DeletedDocumentIds = [];

    public Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken)
    {
        Documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DeletedDocumentIds.Add(documentId);
        Documents.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public readonly List<Func<CancellationToken, Task>> Operations = [];

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        Operations.Add(operation);
        return operation(cancellationToken);
    }
}

internal sealed class RecordingAuditWriter : IOperationAuditWriter
{
    public readonly List<OperationAuditEntry> Entries = [];

    public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingQueue : IDocumentIndexQueue
{
    public readonly List<Guid> EnqueuedIds = [];

    public Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        EnqueuedIds.Add(documentId);
        return Task.CompletedTask;
    }

    public Task<Guid?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult<Guid?>(null);
}

internal sealed class FakeFileStorage : IDocumentFileStorage
{
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

internal sealed class FakeVectorIndex : IVectorIndex
{
    public Task ReplaceDocumentAsync(Domain.Documents.Document document, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class FakeDocumentQueryDal : IDocumentQueryDal
{
    public readonly Dictionary<Guid, DocumentSummary> Documents = [];

    public Task<DocumentSummary?> FindSummaryAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Documents.GetValueOrDefault(id));

    public Task<IReadOnlyList<DocumentSummary>> ListByKbAsync(Guid kbId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DocumentSummary>>(Documents.Values.Where(d => d.KbId == kbId).ToList());

    public Task<IReadOnlyList<Guid>> ListNonProcessingIdsByKbAsync(Guid kbId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(Documents.Values
            .Where(d => d.KbId == kbId && d.Status != DocumentStatus.Processing)
            .Select(d => d.Id).ToList());

    public Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(Documents.Values
            .Where(d => d.Status == DocumentStatus.Pending)
            .Select(d => d.Id).ToList());

    public Task<IReadOnlyList<Guid>> ListTimedOutProcessingIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(Documents.Values
            .Where(d => d.Status == DocumentStatus.Processing && d.UpdatedAt < olderThan)
            .Select(d => d.Id).ToList());
}

internal sealed class FakeUserLookup : IUserLookup
{
    public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken)
        => Task.FromResult<string?>("user-" + userId.ToString()[..8]);
}

internal sealed class StubLogger : ILogger<KnowledgeBaseService>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

