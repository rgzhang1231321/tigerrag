using Microsoft.Extensions.Logging;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents;

/// <summary>
/// 文档索引编排：Pending → Processing → Indexed / Failed。
/// 采用最终一致性模型：DB 事务覆盖"状态变更 + 审计"，外部 I/O（MinIO/Embedding/Qdrant）不在事务内。
/// </summary>
public class DocumentIndexingService(
    IDocumentRepository documents,
    IDocumentLifecycleDal lifecycleDal,
    IDocumentFileStorage fileStorage,
    IDocumentParser parser,
    ITextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    IVectorIndex vectorIndex,
    IUnitOfWork unitOfWork,
    IOperationAuditWriter audit,
    ILogger<DocumentIndexingService> logger)
{
    /// <summary>
    /// 处理一条已被 Worker 认领的文档。前置契约：调用方已通过 <c>TryClaimAsync</c> 把 Status 原子置为 Processing。
    /// </summary>
    public async Task IndexAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // 阶段 1：DB 事务，取文档 + 写审计。
        Document? document = null;
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            document = await documents.FindAsync(documentId, innerCt)
                ?? throw new InvalidOperationException($"Document {documentId} was not found.");

            // 不变量：Worker 已认领，状态必须是 Processing。
            if (document.Status != DocumentStatus.Processing)
            {
                throw new InvalidOperationException(
                    $"Document {documentId} status is {document.Status}, expected Processing.");
            }

            await audit.RecordAsync(new OperationAuditEntry(
                Guid.Empty, "system",
                OperationAuditActions.DocumentIndexStart,
                "document", documentId.ToString(),
                document.FileName), innerCt);
        }, cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        // 阶段 2：外部 I/O（最终一致；失败进入阶段 4 写 Failed）。
        try
        {
            await using var file = await fileStorage.OpenReadAsync(document.StoragePath, cancellationToken);
            var content = await parser.ParseAsync(file, document.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : null, cancellationToken);
            var chunks = chunker.Split(content.Content);
            var vectors = await embeddingGenerator.GenerateAsync(chunks, cancellationToken);
            await vectorIndex.ReplaceDocumentAsync(document, chunks, vectors, cancellationToken);

            // 阶段 3：DB 事务，写 Indexed + 审计。
            await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var doc = await documents.FindAsync(documentId, innerCt)
                    ?? throw new InvalidOperationException($"Document {documentId} was not found.");
                doc.CompleteIndexing(chunks.Count);
                await documents.SaveAsync(doc, innerCt);
                await audit.RecordAsync(new OperationAuditEntry(
                    Guid.Empty, "system",
                    OperationAuditActions.DocumentIndexSuccess,
                    "document", documentId.ToString(),
                    $"{doc.FileName} 分块 {chunks.Count}"), innerCt);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // 阶段 4：DB 事务，写 Failed + 审计。CancellationToken.None 防止取消破坏错误留痕。
            try
            {
                await unitOfWork.ExecuteAsync(async innerCt =>
                {
                    var doc = await documents.FindAsync(documentId, innerCt);
                    if (doc is null) return;
                    doc.FailIndexing(TrimMessage(ex));
                    await documents.SaveAsync(doc, innerCt);
                    await audit.RecordAsync(new OperationAuditEntry(
                        Guid.Empty, "system",
                        OperationAuditActions.DocumentIndexFailed,
                        "document", documentId.ToString(),
                        $"{doc.FileName}：{TrimMessage(ex)}"), innerCt);
                }, CancellationToken.None);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "写入索引失败审计时发生二次异常 Document={DocId}", documentId);
            }
        }
    }

    /// <summary>把异常消息截断到合理长度，避免审计 Summary 字段溢出。</summary>
    private static string TrimMessage(Exception ex)
    {
        const int maxLength = 400;
        var message = ex.Message;
        return message.Length > maxLength ? message[..maxLength] : message;
    }
}