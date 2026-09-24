using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TigerRAG.Api.Common;
using TigerRAG.Api.Filters;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 验证 ApiResponseFilter 对 MVC 自动产生的 ValidationProblemDetails 的展开行为：
/// 字段错误必须出现在响应中，否则调试时只能看到泛化的"One or more validation errors occurred"。
/// 同时验证失败信息暂存契约：响应统一改写为 HTTP 200 后，真实状态码与业务码必须留在
/// HttpContext.Items 供 AccessLogMiddleware 记录。
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

    [Fact]
    public async Task OnResultExecutionAsync_BusinessFailureEnvelope_StashesFailureWithRealStatus()
    {
        var httpContext = new DefaultHttpContext();
        var objectResult = new ObjectResult(
            ApiResponse<object?>.Failure(FlagStatesOption.Conflict, "用户名已存在"));

        await ExecuteFilterAsync(httpContext, objectResult);

        // 业务失败对外是 HTTP 200 + code != 0：真实状态与 code/message 必须暂存给访问日志。
        var failure = Assert.IsType<AccessLogFailure>(httpContext.Items[AccessLogKeys.ItemKey]);
        Assert.Equal(StatusCodes.Status200OK, failure.StatusCode);
        Assert.Equal(FlagStatesOption.Conflict, failure.Code);
        Assert.Equal("用户名已存在", failure.Message);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
    }

    [Fact]
    public async Task OnResultExecutionAsync_ValidationProblemWith400_PreservesOriginalStatusBeforeRewrite()
    {
        var httpContext = new DefaultHttpContext();
        var validation = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["userName"] = ["The UserName field is required."]
        })
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        };
        var objectResult = new ObjectResult(validation) { StatusCode = StatusCodes.Status400BadRequest };

        await ExecuteFilterAsync(httpContext, objectResult);

        // 自动 400：原始状态码必须在被改写为 200 之前捕获，否则访问日志只能记到 200。
        var failure = Assert.IsType<AccessLogFailure>(httpContext.Items[AccessLogKeys.ItemKey]);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Equal(FlagStatesOption.Validation, failure.Code);
        Assert.Equal("The UserName field is required.", failure.Message);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
    }

    [Fact]
    public async Task OnResultExecutionAsync_SuccessEnvelope_DoesNotStashFailure()
    {
        var httpContext = new DefaultHttpContext();
        var objectResult = new ObjectResult(ApiResponse<object?>.Success(new { ok = true }));

        await ExecuteFilterAsync(httpContext, objectResult);

        // 成功请求不暂存，避免访问日志把成功误记为失败。
        Assert.False(httpContext.Items.ContainsKey(AccessLogKeys.ItemKey));
    }

    [Fact]
    public async Task OnResultExecutionAsync_StatusCodeResult404_StashesFailureWithStatus()
    {
        var httpContext = new DefaultHttpContext();

        var context = new ResultExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            filters: [],
            result: new NotFoundResult(),
            controller: new object());
        var filter = new ApiResponseFilter(NullLogger<ApiResponseFilter>.Instance);

        await filter.OnResultExecutionAsync(context, () => Task.FromResult(new ResultExecutedContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            filters: [],
            context.Result,
            controller: new object())));

        // StatusCodeResult 分支同样暂存：改写后真实状态码再无处可寻。
        var failure = Assert.IsType<AccessLogFailure>(httpContext.Items[AccessLogKeys.ItemKey]);
        Assert.Equal(StatusCodes.Status404NotFound, failure.StatusCode);
        Assert.Equal(FlagStatesOption.NotFound, failure.Code);
    }

    /// <summary>构造 ResultExecutingContext 并执行过滤器，模拟 MVC 结果过滤器阶段。</summary>
    private static async Task ExecuteFilterAsync(DefaultHttpContext httpContext, ObjectResult objectResult)
    {
        var filter = new ApiResponseFilter(NullLogger<ApiResponseFilter>.Instance);
        var context = new ResultExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            filters: [],
            objectResult,
            controller: new object());

        await filter.OnResultExecutionAsync(context, () => Task.FromResult(new ResultExecutedContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            filters: [],
            context.Result,
            controller: new object())));
    }
}
