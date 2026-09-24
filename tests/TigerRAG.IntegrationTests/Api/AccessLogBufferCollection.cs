namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 访问日志相关测试共享静态 <c>AccessLogBuffer</c>，必须串行执行避免相互污染缓冲状态。
/// </summary>
[CollectionDefinition(nameof(AccessLogBufferCollection), DisableParallelization = true)]
public sealed class AccessLogBufferCollection
{
}
