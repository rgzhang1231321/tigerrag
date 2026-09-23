using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;
using TigerRAG.Domain.KnowledgeBases;
using Microsoft.Extensions.Logging;

namespace TigerRAG.UnitTests.Documents;

public sealed class DocumentServiceTests
{
    private static readonly ActorContext Owner = new(Guid.NewGuid(), "owner");
    private static readonly ActorContext Stranger = new(Guid.NewGuid(), "stranger");
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UploadAsync_InsertsPendingAndEnqueues()
    {
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        var kbDal = new InMemoryKbDal();
        kbDal.Seed(kb);
        var lifecycle = new FakeLifecycleDal();
        var query = new FakeQueryDal();
        var queue = new RecordingQueue();
        var storage = new RecordingFileStorage();
        var uow = new FakeUnitOfWork();
        var audit = new RecordingAuditWriter();
        var service = CreateService(kbDal, lifecycle, query, queue, storage, uow, audit);

        using var stream = new MemoryStream("hello"u8.ToArray());
        var request = new UploadDocumentRequest(kb.Id, "hello.txt", "text/plain", stream.Length, stream);
        var result = await service.UploadAsync(request, Owner, CancellationToken.None);

        Assert.Equal(DocumentStatus.Pending.ToString(), result.Status);
        Assert.Equal(1, lifecycle.Documents.Count);
        Assert.Equal(1, queue.EnqueuedIds.Count);
        Assert.Equal(1, storage.WrittenPaths.Count);
        Assert.Equal(OperationAuditActions.DocumentCreate, audit.Entries[0].Action);
    }

    [Fact]
    public async Task UploadAsync_UnsupportedMimeThrows()
    {
        var service = CreateService(new InMemoryKbDal());
        using var stream = new MemoryStream("x"u8.ToArray());
        var request = new UploadDocumentRequest(Guid.NewGuid(), "x.zip", "application/zip", 1, stream);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UploadAsync(request, Owner, CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_KbNotFoundThrows()
    {
        var service = CreateService(new InMemoryKbDal());
        using var stream = new MemoryStream("x"u8.ToArray());
        var request = new UploadDocumentRequest(Guid.NewGuid(), "x.txt", "text/plain", 1, stream);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.UploadAsync(request, Owner, CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_NonOwnerThrows()
    {
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        var kbDal = new InMemoryKbDal();
        kbDal.Seed(kb);
        var service = CreateService(kbDal);
        using var stream = new MemoryStream("x"u8.ToArray());
        var request = new UploadDocumentRequest(kb.Id, "x.txt", "text/plain", 1, stream);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.UploadAsync(request, Stranger, CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_SizeOverLimitThrows()
    {
        var service = CreateService(new InMemoryKbDal());
        using var stream = new MemoryStream(new byte[1024]);
        var request = new UploadDocumentRequest(Guid.NewGuid(), "x.txt", "text/plain", 31L * 1024 * 1024, stream);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UploadAsync(request, Owner, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_CascadesChunksAndPermissions()
    {
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var lifecycle = new FakeLifecycleDal();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, kbId, "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        var service = CreateService(new InMemoryKbDal(), lifecycle, query, aclAll: true);

        await service.DeleteAsync(docId, Owner, [], isAdmin: true, CancellationToken.None);

        Assert.Equal(1, lifecycle.DeletedChunks.Count);
        Assert.Equal(1, lifecycle.DeletedPermissions.Count);
        Assert.Equal(1, lifecycle.DeletedDocuments.Count);
    }

    [Fact]
    public async Task DeleteAsync_ProcessingDocument_Rejects()
    {
        var docId = Guid.NewGuid();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, Guid.NewGuid(), "a.txt", null, "p1", DocumentStatus.Processing, 0, null, Owner.Id, Now, Now);
        var service = CreateService(new InMemoryKbDal(), new FakeLifecycleDal(), query, aclAll: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(docId, Owner, [], isAdmin: true, CancellationToken.None));
    }

    [Fact]
    public async Task ReindexAsync_ResetsAndAudits()
    {
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        var kbDal = new InMemoryKbDal();
        kbDal.Seed(kb);
        var lifecycle = new FakeLifecycleDal();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, kb.Id, "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        var queue = new RecordingQueue();
        var audit = new RecordingAuditWriter();
        var service = CreateService(kbDal, lifecycle, query, queue, audit: audit);

        await service.ReindexAsync(docId, Owner, [], isAdmin: false, CancellationToken.None);

        Assert.Equal(1, lifecycle.ReindexedDocuments.Count);
        Assert.Equal(1, queue.EnqueuedIds.Count);
        Assert.Equal(OperationAuditActions.DocumentReindex, audit.Entries[0].Action);
    }

    [Fact]
    public async Task ReindexAsync_ProcessingDocument_Rejects()
    {
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, kbId, "a.txt", null, "p1", DocumentStatus.Processing, 0, null, Owner.Id, Now, Now);
        var service = CreateService(new InMemoryKbDal(), new FakeLifecycleDal(), query, aclAll: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReindexAsync(docId, Owner, [], isAdmin: true, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AdminBypassesAcl()
    {
        var docId = Guid.NewGuid();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, Guid.NewGuid(), "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        var service = CreateService(new InMemoryKbDal(), new FakeLifecycleDal(), query, aclAll: true);

        var result = await service.GetAsync(docId, Stranger, [], isAdmin: true, CancellationToken.None);

        Assert.Equal(docId, result.Id);
    }

    [Fact]
    public async Task GetAsync_AclUnauthorizedUserForbidden()
    {
        var docId = Guid.NewGuid();
        var query = new FakeQueryDal();
        query.Documents[docId] = new DocumentSummary(
            docId, Guid.NewGuid(), "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        var service = CreateService(new InMemoryKbDal(), new FakeLifecycleDal(), query, aclAll: false, aclIds: []);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAsync(docId, Stranger, [], isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_RespectsAcl()
    {
        var kbId = Guid.NewGuid();
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var kb = KnowledgeBase.Create("KB", null, Owner.Id, Now);
        var kbDal = new InMemoryKbDal();
        kbDal.Seed(kb);
        var query = new FakeQueryDal();
        query.Documents[docId1] = new DocumentSummary(
            docId1, kb.Id, "a.txt", null, "p1", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        query.Documents[docId2] = new DocumentSummary(
            docId2, kb.Id, "b.txt", null, "p2", DocumentStatus.Indexed, 3, null, Owner.Id, Now, Now);
        var service = CreateService(kbDal, new FakeLifecycleDal(), query, aclAll: false, aclIds: [docId1]);

        var page = await service.ListAsync(kb.Id, Owner, [], isAdmin: false, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(docId1, page.Items[0].Id);
    }

    private static DocumentService CreateService(
        IKbDal kbDal,
        IDocumentLifecycleDal? lifecycle = null,
        IDocumentQueryDal? query = null,
        IDocumentIndexQueue? queue = null,
        IDocumentFileStorage? storage = null,
        IUnitOfWork? uow = null,
        IOperationAuditWriter? audit = null,
        bool aclAll = false,
        IReadOnlyList<Guid>? aclIds = null)
    {
        var accessDal = new FakeDocumentAccess(aclAll, aclIds ?? []);
        var accessService = new DocumentAccessService(accessDal, new FakeRoleRegistry(aclAll));
        return new DocumentService(
            kbDal,
            lifecycle ?? new FakeLifecycleDal(),
            query ?? new FakeQueryDal(),
            queue ?? new RecordingQueue(),
            storage ?? new RecordingFileStorage(),
            new FakeVectorIndex(),
            accessService,
            uow ?? new FakeUnitOfWork(),
            audit ?? new RecordingAuditWriter(),
            new StubLogger());
    }
}

// ---- Fakes ----

internal sealed class FakeLifecycleDal : IDocumentLifecycleDal
{
    public readonly Dictionary<Guid, DocumentSummary> Documents = [];
    public readonly List<Guid> DeletedChunks = [];
    public readonly List<Guid> DeletedPermissions = [];
    public readonly List<Guid> DeletedDocuments = [];
    public readonly List<Guid> ReindexedDocuments = [];

    public Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken)
    {
        Documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DeletedChunks.Add(documentId);
        return Task.CompletedTask;
    }

    public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DeletedPermissions.Add(documentId);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DeletedDocuments.Add(documentId);
        Documents.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        ReindexedDocuments.Add(documentId);
        return Task.FromResult(true);
    }
}

internal sealed class FakeQueryDal : IDocumentQueryDal
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

internal sealed class RecordingFileStorage : IDocumentFileStorage
{
    public readonly List<string> WrittenPaths = [];
    public readonly List<string> DeletedPaths = [];

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> WriteAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        WrittenPaths.Add(path);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        DeletedPaths.Add(path);
        return Task.FromResult(true);
    }
}

internal sealed class FakeVectorIndex : IVectorIndex
{
    public Task ReplaceDocumentAsync(Domain.Documents.Document document, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class FakeDocumentAccess(bool all, IReadOnlyList<Guid> ids) : TigerRAG.Application.Documents.IDocumentAccessDal
{
    public Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        if (all) throw new InvalidOperationException("Admin 已短路返回 AllDocuments=true，不应调用 GetAccessibleDocumentIdsAsync。");
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    public Task ReplacePermissionsAsync(Guid documentId, Guid actorId, bool isAdmin, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class FakeRoleRegistry(bool userHasAdmin = false) : TigerRAG.Application.Roles.IRoleRegistry
{
    public Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken)
        => Task.FromResult(userHasAdmin && name == "Admin");

    public Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(1);
}

internal sealed class InMemoryKbDal : IKbDal
{
    public readonly Dictionary<Guid, KnowledgeBase> Entities = [];

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
        => Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task DeleteAsync(Guid kbId, CancellationToken cancellationToken)
    {
        Entities.Remove(kbId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListNonProcessingDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<int> ResetNonProcessingToPendingAsync(Guid kbId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        => Task.FromResult(0);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        => operation(cancellationToken);
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

internal sealed class StubLogger : ILogger<DocumentService>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}