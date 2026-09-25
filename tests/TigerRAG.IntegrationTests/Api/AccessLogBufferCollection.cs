namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 访问日志与消息日志测试共享静态 <c>ApiLogBuffer</c>（单表单通道），
/// 必须串行执行避免相互污染缓冲状态。
/// </summary>
[CollectionDefinition(nameof(AccessLogBufferCollection), DisableParallelization = true)]
public sealed class AccessLogBufferCollection
{
}
