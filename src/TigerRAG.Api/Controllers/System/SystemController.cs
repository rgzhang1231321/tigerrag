using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;

namespace TigerRAG.Api.Controllers.System;

[ApiController]
[Route("api")]
public sealed class SystemController : ControllerBase
{
    /// <summary>
    /// 获取 TigerRAG API 的服务名称和当前接口版本。
    /// </summary>
    /// <returns>API 服务描述信息。</returns>
    [HttpGet]
    public ActionResult<ApiResponse<ApiDescriptor>> GetDescriptor() =>
        Ok(ApiResponse.Success(new ApiDescriptor("TigerRAG.Api", "v1")));
}

public sealed record ApiDescriptor(string Name, string Version);
