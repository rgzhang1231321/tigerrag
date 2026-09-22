namespace TigerRAG.Api.Common;

/// <summary>
/// 标识已经被 ApiResponse 外壳包装过的响应对象，供过滤器去重与注入 RequestId。
/// </summary>
public interface ApiResponseMarker
{
    object WithRequestId(string requestId);
}