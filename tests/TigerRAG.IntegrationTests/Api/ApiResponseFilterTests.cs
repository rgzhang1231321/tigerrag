using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TigerRAG.Api.Common;
using TigerRAG.Api.Filters;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 验证 ApiResponseFilter 对 MVC 自动产生的 ValidationProblemDetails 的展开行为：
/// 字段错误必须出现在响应中，否则调试时只能看到泛化的"One or more validation errors occurred"。
/// </summary>
public sealed class ApiResponseFilterTests
{
    [Fact]
    public void WrapObjectResult_ValidationProblemDetails_SurfacesFirstErrorInMessageAndAllErrorsInData()
    {
        var validation = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["userName"] = ["The UserName field is required."],
            ["passwordHash"] = ["The PasswordHash field is required."]
        })
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        };
        var logger = NullLogger<ApiResponseFilter>.Instance;

        var wrapped = ApiResponseFilter.WrapObjectResult(new ObjectResult(validation), logger);

        var response = Assert.IsType<ApiResponse<object?>>(wrapped);
        Assert.Equal(FlagStatesOption.Validation, response.Code);
        // 首条错误升级为 Message，避免调试者只看到泛化文案。
        Assert.Equal("The UserName field is required.", response.Message);
        // 完整字段错误字典放进 Data，前端可按字段展示；调试者也可在响应体里直接看到所有原因。
        var data = Assert.IsType<Dictionary<string, string[]>>(response.Data);
        Assert.Equal("The UserName field is required.", data["userName"][0]);
        Assert.Equal("The PasswordHash field is required.", data["passwordHash"][0]);
    }

    [Fact]
    public void WrapObjectResult_GenericProblemDetails_KeepsDetailAsMessageWithoutData()
    {
        var problem = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "Resource xyz was not found.",
            Status = StatusCodes.Status404NotFound
        };
        var logger = NullLogger<ApiResponseFilter>.Instance;

        var wrapped = ApiResponseFilter.WrapObjectResult(new ObjectResult(problem), logger);

        var response = Assert.IsType<ApiResponse<object?>>(wrapped);
        Assert.Equal(FlagStatesOption.NotFound, response.Code);
        Assert.Equal("Resource xyz was not found.", response.Message);
        Assert.Null(response.Data);
    }
}
