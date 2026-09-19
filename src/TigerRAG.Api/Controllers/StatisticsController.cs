using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Api.Controllers;

/// <summary>系统概览 Dashboard 统计：单端点并行聚合核心指标，避免前端 N+1。</summary>
[ApiController]
[Route("api/statistics")]
[Authorize]
public sealed class StatisticsController(TigerRagDbContext dbContext) : ControllerBase
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    [HttpPost("dashboard")]
    public async Task<ActionResult<ApiResponse<DashboardMetricsResponse>>> GetDashboard(
        CancellationToken cancellationToken)
    {
        // 顺序 await：DbContext 非线程安全，并行计数会触发 "second operation" 异常。
        var documents = dbContext.Documents.AsNoTracking();
        var kbCount = await dbContext.KnowledgeBases.CountAsync(cancellationToken);
        var docCount = await documents.CountAsync(cancellationToken);
        var docIndexed = await documents.CountAsync(d => d.Status == DocumentStatus.Indexed, cancellationToken);
        var docProcessing = await documents.CountAsync(d => d.Status == DocumentStatus.Processing, cancellationToken);
        var docFailed = await documents.CountAsync(d => d.Status == DocumentStatus.Failed, cancellationToken);
        var userCount = await dbContext.Users.CountAsync(cancellationToken);
        var convCount = await dbContext.Conversations.CountAsync(cancellationToken);
        var msgCount = await dbContext.Messages.CountAsync(cancellationToken);

        var response = new DashboardMetricsResponse(
            KnowledgeBaseCount: kbCount,
            DocumentCount: docCount,
            IndexedDocumentCount: docIndexed,
            ProcessingDocumentCount: docProcessing,
            FailedDocumentCount: docFailed,
            UserCount: userCount,
            ConversationCount: convCount,
            MessageCount: msgCount);
        return Ok(ApiResponse.Success(response));
    }
}

/// <summary>Dashboard 指标响应。字段命名稳定，供前端直接消费。</summary>
public sealed record DashboardMetricsResponse(
    int KnowledgeBaseCount,
    int DocumentCount,
    int IndexedDocumentCount,
    int ProcessingDocumentCount,
    int FailedDocumentCount,
    int UserCount,
    int ConversationCount,
    int MessageCount);
