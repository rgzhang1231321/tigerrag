using Xunit;

namespace TigerRAG.UnitTests.Logging;

/// <summary>
/// 把所有访问 <see cref="Infrastructure.Logging.ApiLogBuffer"/> 静态状态的测试串行化，
/// 避免跨测试类的并行执行清空共享 <c>Pending</c> 缓冲导致非确定性失败。
/// </summary>
[CollectionDefinition(nameof(ApiLogBufferCollection), DisableParallelization = true)]
public sealed class ApiLogBufferCollection : ICollectionFixture<ApiLogBufferCollection>
{
}