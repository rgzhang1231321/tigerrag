using Microsoft.Extensions.Logging;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;
using TigerRAG.Domain.KnowledgeBases;

namespace TigerRAG.Application.KnowledgeBases;

/// <summary>知识库业务编排：创建 / 查询 / 更新 / 级联删除 / 重索引。</summary>
public sealed class KnowledgeBaseService(
    IKbDal kbDal,
    IDocumentLifecycleDal documentLifecycleDal,
    IDocumentQueryDal documentQueryDal,
    IDocumentIndexQueue queue,
    IDocumentFileStorage fileStorage,
    IVectorIndex vectorIndex,
    IUnitOfWork unitOfWork,
    IOperationAuditWriter audit,
    IUserLookup userLookup,
    ILogger<KnowledgeBaseService> logger)
{
    /// <summary>创建知识库；事务内插入行 + 审计；事务外补全 OwnerName。</summary>
    public async Task<KnowledgeBaseDto> CreateAsync(
        string name,
        string? description,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var knowledgeBase = KnowledgeBase.Create(name, description, actor.Id, now);

        await unitOfWork.ExecuteAsync(async ct =>
        {
            await kbDal.InsertAsync(knowledgeBase, ct);
            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.KbCreate,
                "knowledgeBase", knowledgeBase.Id.ToString(),
                knowledgeBase.Name), ct);
        }, cancellationToken);

        return await MapToDtoAsync(knowledgeBase, cancellationToken);
    }

    /// <summary>列出知识库：Admin 返回全部，非 Admin 仅返回 OwnerId == actor.Id。逐项补全 OwnerName 和 DocumentCount。</summary>
    public async Task<IReadOnlyList<KnowledgeBaseDto>> ListAsync(
        ActorContext actor,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var summaries = await kbDal.ListAsync(actor.Id, isAdmin, cancellationToken);
        var dtos = new List<KnowledgeBaseDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            dtos.Add(await MapSummaryToDtoAsync(summary, cancellationToken));
        }
        return dtos;
    }

    /// <summary>按 Id 获取知识库详情；非 Admin 必须是 Owner，否则抛 UnauthorizedAccessException。</summary>
    public async Task<KnowledgeBaseDto> GetAsync(
        Guid id,
        ActorContext actor,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = await kbDal.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"知识库 {id} 不存在。");

        if (!isAdmin && knowledgeBase.OwnerId != actor.Id)
        {
            throw new UnauthorizedAccessException("仅知识库拥有者可查看。");
        }

        return await MapToDtoAsync(knowledgeBase, cancellationToken);
    }

    /// <summary>更新知识库元数据（Name/Description/Owner）；事务内更新 + 审计。Name/Description 用 Nullable<T> 区分"未提供"与"清空"。</summary>
    public async Task<KnowledgeBaseDto> UpdateAsync(
        Guid id,
        UpdateKnowledgeBaseRequest request,
        ActorContext actor,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var updated = false;

        await unitOfWork.ExecuteAsync(async ct =>
        {
            var knowledgeBase = await kbDal.FindAsync(id, ct)
                ?? throw new KeyNotFoundException($"知识库 {id} 不存在。");

            if (!isAdmin && knowledgeBase.OwnerId != actor.Id)
            {
                throw new UnauthorizedAccessException("仅知识库拥有者可修改。");
            }

            if (request.Name is { } newName && newName != knowledgeBase.Name)
            {
                knowledgeBase.Rename(newName);
                updated = true;
            }

            if (request.Description is { } newDescription && newDescription != knowledgeBase.Description)
            {
                knowledgeBase.UpdateDescription(newDescription);
                updated = true;
            }
            else if (request.Description is null && knowledgeBase.Description is not null)
            {
                knowledgeBase.UpdateDescription(null);
                updated = true;
            }

            if (request.NewOwnerId is { } newOwner && newOwner != knowledgeBase.OwnerId)
            {
                knowledgeBase.TransferOwnership(newOwner);
                updated = true;
            }

            if (!updated) return;

            await kbDal.UpdateAsync(knowledgeBase, ct);
            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.KbUpdate,
                "knowledgeBase", knowledgeBase.Id.ToString(),
                knowledgeBase.Name), ct);
        }, cancellationToken);

        var result = await kbDal.FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"知识库 {id} 在更新后丢失。");
        return await MapToDtoAsync(result, cancellationToken);
    }

    /// <summary>级联删除知识库：事务内清文档/chunks/permissions/KB 行 + 审计；事务外清理向量和 MinIO 文件。</summary>
    public async Task DeleteAsync(
        Guid id,
        ActorContext actor,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var storagePaths = new List<string>();
        var documentIds = new List<Guid>();

        await unitOfWork.ExecuteAsync(async ct =>
        {
            var knowledgeBase = await kbDal.FindAsync(id, ct)
                ?? throw new KeyNotFoundException($"知识库 {id} 不存在。");

            if (!isAdmin && knowledgeBase.OwnerId != actor.Id)
            {
                throw new UnauthorizedAccessException("仅知识库拥有者可删除。");
            }

            documentIds.AddRange(await kbDal.ListDocumentIdsAsync(id, ct));
            foreach (var docId in documentIds)
            {
                var document = await documentQueryDal.FindSummaryAsync(docId, ct);
                if (document is { Status: DocumentStatus.Processing })
                {
                    throw new InvalidOperationException("文档正在索引中，请等待完成后重试。");
                }
                await documentLifecycleDal.DeleteChunksAsync(docId, ct);
                await documentLifecycleDal.DeletePermissionsAsync(docId, ct);
                await documentLifecycleDal.DeleteAsync(docId, ct);
                if (document is { } doc && !string.IsNullOrEmpty(doc.StoragePath))
                {
                    storagePaths.Add(doc.StoragePath);
                }
            }

            await kbDal.DeleteAsync(id, ct);
            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.KbDelete,
                "knowledgeBase", id.ToString(),
                $"{knowledgeBase.Name}，文档 {documentIds.Count}"), ct);
        }, cancellationToken);

        foreach (var path in storagePaths)
        {
            try
            {
                await fileStorage.DeleteAsync(path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "知识库 {KbId} 删除后清理对象存储 {Path} 失败。", id, path);
            }
        }

        foreach (var docId in documentIds)
        {
            try
            {
                await vectorIndex.DeleteDocumentAsync(docId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "知识库 {KbId} 删除后清理向量 {DocId} 失败。", id, docId);
            }
        }
    }

    /// <summary>重索引整个 KB：事务内把非 Processing 文档批量置为 Pending + 审计；事务外逐个入队（失败仅日志）。</summary>
    public async Task ReindexAsync(
        Guid id,
        ActorContext actor,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await unitOfWork.ExecuteAsync(async ct =>
        {
            var knowledgeBase = await kbDal.FindAsync(id, ct)
                ?? throw new KeyNotFoundException($"知识库 {id} 不存在。");

            if (!isAdmin && knowledgeBase.OwnerId != actor.Id)
            {
                throw new UnauthorizedAccessException("仅知识库拥有者可重索引。");
            }

            var resetCount = await kbDal.ResetNonProcessingToPendingAsync(id, now, ct);
            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.KbReindex,
                "knowledgeBase", id.ToString(),
                $"{knowledgeBase.Name}，重置 {resetCount}"), ct);
        }, cancellationToken);

        var documentIds = await kbDal.ListNonProcessingDocumentIdsAsync(id, cancellationToken);
        foreach (var docId in documentIds)
        {
            try
            {
                await queue.EnqueueAsync(docId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "知识库 {KbId} 重索引入队 {DocId} 失败。", id, docId);
            }
        }
    }

    /// <summary>把领域对象映射为 DTO；DocumentCount 由调用方按场景传入（更新后立即重读场景下为 0）。</summary>
    private async Task<KnowledgeBaseDto> MapToDtoAsync(KnowledgeBase kb, CancellationToken ct)
    {
        var ownerName = await userLookup.GetUserNameAsync(kb.OwnerId, ct);
        return new KnowledgeBaseDto(kb.Id, kb.Name, kb.Description, kb.OwnerId, ownerName, 0, kb.CreatedAt);
    }

    /// <summary>把 DAL 摘要映射为 DTO；DocumentCount 和 OwnerName 已由 DAL 填好。</summary>
    private async Task<KnowledgeBaseDto> MapSummaryToDtoAsync(KnowledgeBaseSummary summary, CancellationToken ct)
    {
        var ownerName = await userLookup.GetUserNameAsync(summary.OwnerId, ct);
        return new KnowledgeBaseDto(summary.Id, summary.Name, summary.Description, summary.OwnerId, ownerName, summary.DocumentCount, summary.CreatedAt);
    }
}

/// <summary>知识库更新请求；Name 为 null 表示不变更，Description 用 Nullable&lt;T&gt; 区分"未提供"与"清空"。</summary>
public sealed record UpdateKnowledgeBaseRequest(
    string? Name,
    string? Description,
    Guid? NewOwnerId);

/// <summary>知识库详情 DTO。</summary>
public sealed record KnowledgeBaseDto(
    Guid Id,
    string Name,
    string? Description,
    Guid OwnerId,
    string? OwnerName,
    int DocumentCount,
    DateTimeOffset CreatedAt);