namespace TigerRAG.Application.Documents;

/// <summary>文档列表查询请求；KbId 必填，Page/PageSize 由 Controller 默认填入。</summary>
public sealed record ListDocumentsRequest(
    Guid KbId,
    int Page,
    int PageSize);

/// <summary>文档上传请求；Content 为已读取的文件流，由 Controller 从 IFormFile 转换。</summary>
public sealed record UploadDocumentRequest(
    Guid KbId,
    string FileName,
    string MimeType,
    long Size,
    Stream Content);

/// <summary>文档详情 DTO；不暴露 EF 实体。</summary>
public sealed record DocumentDto(
    Guid Id,
    Guid KbId,
    string FileName,
    string Status,
    int ChunkCount,
    string? FailureReason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>文档分页响应。</summary>
public sealed record DocumentListPage(
    IReadOnlyList<DocumentDto> Items,
    int Page,
    int PageSize,
    int Total);