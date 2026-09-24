using Xunit;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 让所有权限矩阵集成测试串行执行，避免并发操作 role_endpoint_grant 表导致数据竞争。
/// </summary>
[CollectionDefinition("PermissionMatrixIntegration")]
public sealed class PermissionMatrixIntegrationCollection { }
