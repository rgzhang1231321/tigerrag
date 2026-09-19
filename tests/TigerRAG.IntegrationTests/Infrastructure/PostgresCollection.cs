using Xunit;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 让所有在同一台本地 Postgres 上做 DROP/CREATE DATABASE 的集成测试串行执行，
/// 避免 xUnit 并发跑出 connect-twice / drop-while-open 等瞬态故障。
/// </summary>
[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection { }