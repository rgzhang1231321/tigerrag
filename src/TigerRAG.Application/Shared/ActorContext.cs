namespace TigerRAG.Application.Shared;

/// <summary>当前操作人上下文，由 Controller 从 HttpContext 提取后传入 Service。</summary>
public sealed record ActorContext(Guid Id, string Name);
