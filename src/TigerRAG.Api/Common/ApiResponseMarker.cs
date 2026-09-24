namespace TigerRAG.Api.Common;

/// <summary>
/// 标识已经被 ApiResponse 外壳包装过的响应对象，供过滤器去重与注入 RequestId。
/// </summary>
public interface ApiResponseMarker
{
    /// <summary>业务状态码；Success 表示成功，其余值供访问日志识别业务失败。</summary>
    FlagStatesOption Code { get; }

    /// <summary>业务消息；失败时是给用户看的失败原因。</summary>
    string Message { get; }

    object WithRequestId(string requestId);
}