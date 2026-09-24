using System.Text;
using Microsoft.Extensions.Logging;
using TigerRAG.Application.Documents.Indexing;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents;

/// <summary>文档业务编排：列表 / 上传 / 详情 / 删除 / 重索引。</summary>
public sealed class DocumentService(
    IKbDal kbDal,
    IDocumentLifecycleDal documentLifecycleDal,
    IDocumentQueryDal documentQueryDal,
    IDocumentIndexQueue queue,
    IDocumentFileStorage fileStorage,
    IVectorIndex vectorIndex,
    DocumentAccessService documentAccess,
    IUnitOfWork unitOfWork,
    IOperationAuditWriter audit,
    ILogger<DocumentService> logger)
{
    /// <summary>允许上传的 MIME 集合（白名单）。</summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/markdown",
        "text/plain",
    };

    /// <summary>单文档大小上限（30MB）；超过即拒绝。</summary>
    private const long MaxFileSizeBytes = 30L * 1024 * 1024;

    /// <summary>列出指定 KB 下当前用户可见的文档（按 ACL 过滤）；Admin 短路放行。</summary>
    public async Task<DocumentListPage> ListAsync(
        Guid kbId,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        if (await kbDal.FindAsync(kbId, cancellationToken) is null)
        {
            throw new KeyNotFoundException($"知识库 {kbId} 不存在。");
        }

        var scope = await documentAccess.GetScopeAsync(actor.Id, roles, cancellationToken);

        if (scope.AllDocuments)
        {
            var all = await documentQueryDal.ListByKbAsync(kbId, cancellationToken);
            return BuildPage(all, page: 1, pageSize: all.Count);
        }

        var accessibleIds = scope.DocumentIds.ToHashSet();
        var filtered = await documentQueryDal.ListByKbAsync(kbId, cancellationToken);
        filtered = filtered.Where(d => accessibleIds.Contains(d.Id)).ToList();
        return BuildPage(filtered, page: 1, pageSize: filtered.Count);
    }

    /// <summary>上传文档到指定 KB；写 MinIO → DB 插入（Pending） → 入队。</summary>
    public async Task<DocumentDto> UploadAsync(
        UploadDocumentRequest request,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        ValidateUpload(request);

        var knowledgeBase = await kbDal.FindAsync(request.KbId, cancellationToken)
            ?? throw new KeyNotFoundException($"知识库 {request.KbId} 不存在。");

        if (knowledgeBase.OwnerId != actor.Id)
        {
            throw new UnauthorizedAccessException("仅知识库拥有者可上传文档。");
        }

        var now = DateTimeOffset.UtcNow;
        var documentId = Guid.NewGuid();
        var storagePath = $"kb/{request.KbId}/{documentId:N}/{request.FileName}";

        await fileStorage.WriteAsync(storagePath, request.Content, request.MimeType, cancellationToken);

        try
        {
            var summary = new DocumentSummary(
                documentId,
                request.KbId,
                request.FileName,
                request.MimeType,
                storagePath,
                request.Size,
                DocumentStatus.Pending,
                ChunkCount: 0,
                FailureReason: null,
                CreatedBy: actor.Id,
                CreatedAt: now,
                UpdatedAt: now);

            await unitOfWork.ExecuteAsync(async ct =>
            {
                await documentLifecycleDal.InsertAsync(summary, ct);
                await audit.RecordAsync(new OperationAuditEntry(
                    actor.Id, actor.Name,
                    OperationAuditActions.DocumentCreate,
                    "document", documentId.ToString(),
                    request.FileName), ct);
            }, cancellationToken);

            try
            {
                await queue.EnqueueAsync(documentId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "文档 {DocId} 入队失败，依赖启动恢复兜底。", documentId);
            }
        }
        catch
        {
            try { await fileStorage.DeleteAsync(storagePath, CancellationToken.None); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "文档上传失败，补偿清理对象存储 {Path} 失败。", storagePath);
            }
            throw;
        }

        // 直接基于本次插入的摘要构造 DTO，避免再次查询引入未就绪读取。
        return ToDto(new DocumentSummary(
            documentId,
            request.KbId,
            request.FileName,
            request.MimeType,
            storagePath,
            request.Size,
            DocumentStatus.Pending,
            ChunkCount: 0,
            FailureReason: null,
            CreatedBy: actor.Id,
            CreatedAt: now,
            UpdatedAt: now));
    }

    /// <summary>按 Id 获取文档详情；ACL 校验非 Owner 用户的可读性。</summary>
    public async Task<DocumentDto> GetAsync(
        Guid id,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var summary = await documentQueryDal.FindSummaryAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"文档 {id} 不存在。");

        if (!isAdmin)
        {
            var scope = await documentAccess.GetScopeAsync(actor.Id, roles, cancellationToken);
            if (!scope.AllDocuments && !scope.DocumentIds.Contains(id))
            {
                throw new UnauthorizedAccessException("无权限访问此文档。");
            }
        }

        return ToDto(summary);
    }

    /// <summary>删除文档：级联清 chunks/permissions/向量/MinIO。</summary>
    public async Task DeleteAsync(
        Guid id,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        string? storagePath = null;

        await unitOfWork.ExecuteAsync(async ct =>
        {
            var document = await documentQueryDal.FindSummaryAsync(id, ct)
                ?? throw new KeyNotFoundException($"文档 {id} 不存在。");

            if (!isAdmin)
            {
                var scope = await documentAccess.GetScopeAsync(actor.Id, roles, ct);
                var isOwner = await IsKbOwnerAsync(document.KbId, actor.Id, ct);
                if (!isOwner && (!scope.AllDocuments && !scope.DocumentIds.Contains(id)))
                {
                    throw new UnauthorizedAccessException("无权限删除此文档。");
                }
            }

            if (document.Status == DocumentStatus.Processing)
            {
                throw new InvalidOperationException("文档正在索引中，请等待完成后重试。");
            }

            await documentLifecycleDal.DeleteChunksAsync(id, ct);
            await documentLifecycleDal.DeletePermissionsAsync(id, ct);
            await documentLifecycleDal.DeleteAsync(id, ct);
            storagePath = document.StoragePath;

            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.DocumentDelete,
                "document", id.ToString(),
                document.FileName), ct);
        }, cancellationToken);

        // 事务外清理外部副作用：失败仅日志，DB 状态为真相源。
        try { await vectorIndex.DeleteDocumentAsync(id, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "文档 {DocId} 删除后清理向量失败。", id); }

        if (!string.IsNullOrEmpty(storagePath))
        {
            try { await fileStorage.DeleteAsync(storagePath, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "文档 {DocId} 删除后清理对象存储 {Path} 失败。", id, storagePath); }
        }
    }

    /// <summary>批量删除文档：逐个校验权限并级联清理；事务内执行，任一失败整体回滚。</summary>
    public async Task<int> BatchDeleteAsync(
        IReadOnlyList<Guid> ids,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            throw new ArgumentException("未选择任何文档。", nameof(ids));
        }

        var storagePaths = new List<string>();
        var deletedNames = new List<string>();

        await unitOfWork.ExecuteAsync(async ct =>
        {
            foreach (var id in ids)
            {
                var document = await documentQueryDal.FindSummaryAsync(id, ct)
                    ?? throw new KeyNotFoundException($"文档 {id} 不存在。");

                if (!isAdmin)
                {
                    var scope = await documentAccess.GetScopeAsync(actor.Id, roles, ct);
                    var isOwner = await IsKbOwnerAsync(document.KbId, actor.Id, ct);
                    if (!isOwner && (!scope.AllDocuments && !scope.DocumentIds.Contains(id)))
                    {
                        throw new UnauthorizedAccessException($"无权限删除文档「{document.FileName}」。");
                    }
                }

                if (document.Status == DocumentStatus.Processing)
                {
                    throw new InvalidOperationException($"文档「{document.FileName}」正在索引中，请等待完成后重试。");
                }

                await documentLifecycleDal.DeleteChunksAsync(id, ct);
                await documentLifecycleDal.DeletePermissionsAsync(id, ct);
                await documentLifecycleDal.DeleteAsync(id, ct);
                if (!string.IsNullOrEmpty(document.StoragePath))
                {
                    storagePaths.Add(document.StoragePath);
                }
                deletedNames.Add(document.FileName);
            }

            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.DocumentBatchDelete,
                "document", string.Join(",", ids),
                string.Join(", ", deletedNames)), ct);
        }, cancellationToken);

        foreach (var id in ids)
        {
            try { await vectorIndex.DeleteDocumentAsync(id, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "文档 {DocId} 删除后清理向量失败。", id); }
        }

        foreach (var path in storagePaths)
        {
            try { await fileStorage.DeleteAsync(path, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "文档删除后清理对象存储 {Path} 失败。", path); }
        }

        return ids.Count;
    }

    /// <summary>预览内容字节上限（1MB）；超出则截断并标记 Truncated。</summary>
    private const int MaxPreviewBytes = 1024 * 1024;

    /// <summary>当前支持预览的 MIME 白名单；仅 text/plain。</summary>
    private static readonly IReadOnlySet<string> PreviewableMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
    };

    /// <summary>获取文档预览内容：校验权限 → 校验 MIME → 读取对象存储 → UTF-8 解码。超出 MaxPreviewBytes 时截断并标记 Truncated=true。</summary>
    public async Task<DocumentContentDto> GetContentAsync(
        Guid documentId,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var summary = await documentQueryDal.FindSummaryAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"文档 {documentId} 不存在。");

        if (!isAdmin)
        {
            var scope = await documentAccess.GetScopeAsync(actor.Id, roles, cancellationToken);
            if (!scope.AllDocuments && !scope.DocumentIds.Contains(documentId))
            {
                throw new UnauthorizedAccessException("无权限访问此文档。");
            }
        }

        if (summary.MimeType is null || !PreviewableMimeTypes.Contains(summary.MimeType))
        {
            throw new InvalidOperationException($"该文件类型（{summary.MimeType ?? "未知"}）暂不支持预览。");
        }

        var truncated = false;
        string content;
        await using var stream = await fileStorage.OpenReadAsync(summary.StoragePath, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (summary.Size <= MaxPreviewBytes)
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            var buffer = new char[MaxPreviewBytes];
            var totalRead = 0;
            while (totalRead < MaxPreviewBytes)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(totalRead, MaxPreviewBytes - totalRead), cancellationToken);
                if (read == 0) break;
                totalRead += read;
            }
            content = new string(buffer, 0, totalRead);
            truncated = true;
        }

        return new DocumentContentDto(
            summary.Id,
            summary.FileName,
            summary.MimeType,
            content,
            summary.Size,
            truncated,
            truncated ? MaxPreviewBytes : null);
    }

    /// <summary>单篇重索引：Processing 时拒绝；否则 Status 重置为 Pending 并入队。</summary>
    public async Task ReindexAsync(
        Guid id,
        ActorContext actor,
        IReadOnlyCollection<string> roles,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await unitOfWork.ExecuteAsync(async ct =>
        {
            var document = await documentQueryDal.FindSummaryAsync(id, ct)
                ?? throw new KeyNotFoundException($"文档 {id} 不存在。");

            if (!isAdmin)
            {
                var isOwner = await IsKbOwnerAsync(document.KbId, actor.Id, ct);
                if (!isOwner)
                {
                    throw new UnauthorizedAccessException("仅知识库拥有者可重索引文档。");
                }
            }

            if (document.Status == DocumentStatus.Processing)
            {
                throw new InvalidOperationException("文档正在索引中，请等待完成后重试。");
            }

            var reset = await documentLifecycleDal.MarkReindexAsync(id, now, ct);
            if (!reset)
            {
                throw new InvalidOperationException($"文档 {id} 状态变更失败。");
            }

            await audit.RecordAsync(new OperationAuditEntry(
                actor.Id, actor.Name,
                OperationAuditActions.DocumentReindex,
                "document", id.ToString(),
                document.FileName), ct);
        }, cancellationToken);

        try
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "文档 {DocId} 重索引入队失败，依赖启动恢复兜底。", id);
        }
    }

    /// <summary>校验上传参数：文件名、MIME 白名单、大小上下界；不合法抛 ArgumentException。</summary>
    private static void ValidateUpload(UploadDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("文件名不能为空。", nameof(request));
        }
        if (request.FileName.Length > 500)
        {
            throw new ArgumentException("文件名长度不能超过 500。", nameof(request));
        }
        if (!AllowedMimeTypes.Contains(request.MimeType))
        {
            throw new ArgumentException($"不支持的 MIME 类型 {request.MimeType}。", nameof(request));
        }
        if (request.Size <= 0)
        {
            throw new ArgumentException("文件不能为空。", nameof(request));
        }
        if (request.Size > MaxFileSizeBytes)
        {
            throw new ArgumentException($"文件大小 {request.Size} 字节超过 {MaxFileSizeBytes} 字节上限。", nameof(request));
        }
    }

    /// <summary>判断 <paramref name="userId"/> 是否是指定 KB 的 Owner；KB 不存在时返回 false。</summary>
    private async Task<bool> IsKbOwnerAsync(Guid kbId, Guid userId, CancellationToken cancellationToken)
    {
        var kb = await kbDal.FindAsync(kbId, cancellationToken);
        return kb is not null && kb.OwnerId == userId;
    }

    /// <summary>把已过滤的摘要集合封装成分页响应。当前 Service 一次性返回，Page/PageSize 透传给前端做展示。</summary>
    private static DocumentListPage BuildPage(IReadOnlyList<DocumentSummary> items, int page, int pageSize)
    {
        var dtos = items.Select(ToDto).ToList();
        return new DocumentListPage(dtos, page, pageSize, dtos.Count);
    }

    /// <summary>把 DocumentSummary 映射为 DocumentDto；状态使用字符串形式便于前端直接展示。</summary>
    private static DocumentDto ToDto(DocumentSummary summary) => new(
        summary.Id,
        summary.KbId,
        summary.FileName,
        summary.Status.ToString(),
        summary.ChunkCount,
        summary.Size,
        summary.FailureReason,
        summary.CreatedBy,
        summary.CreatedAt,
        summary.UpdatedAt);
}