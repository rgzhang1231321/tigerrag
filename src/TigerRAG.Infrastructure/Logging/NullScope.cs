namespace TigerRAG.Infrastructure.Logging;

/// <summary>占位 ILogger Scope：<see cref="TigerRagSqlLogger"/> 不支持 scope，返回此实例丢弃所有写入。</summary>
internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();
    public void Dispose() { }
}