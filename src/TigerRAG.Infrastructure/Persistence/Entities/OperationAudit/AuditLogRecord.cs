namespace TigerRAG.Infrastructure.Persistence.Entities.OperationAudit;

/// <summary>审计日志持久化记录。<see cref="Query"/> / <see cref="Answer"/> 用于问答审计回放。</summary>
public sealed class audit_log_record
{
    /// <summary>审计日志主键。</summary>
    public Guid Id { get; set; }

    /// <summary>提问用户 Id。</summary>
    public Guid UserId { get; set; }

    /// <summary>用户原始提问文本。</summary>
    public required string Query { get; set; }

    /// <summary>检索阶段命中的文档 Id 集合，用于审计回放时定位证据。</summary>
    public Guid[] RetrievedDocumentIds { get; set; } = [];

    /// <summary>系统生成的回答（流式场景在最终包到达前为空）。</summary>
    public string? Answer { get; set; }

    /// <summary>来源 IP（反向代理后的客户端 IP）。</summary>
    public string? IpAddress { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
