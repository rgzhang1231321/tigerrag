using System.Reflection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

using TigerRAG.Application.Auth;
using TigerRAG.Infrastructure.Auth;

namespace TigerRAG.UnitTests.Auth;

/// <summary>扫描器发现 [MenuEndpoint] 行为：描述必填、HTTP 方法/路径正确读取、Find 命中与未命中。</summary>
public sealed class MenuEndpointRegistryTests
{
    private sealed class FakeDocumentsController
    {
        [MenuEndpoint(menuKey: "documents", endpointKey: "documents.fake", description: "测试 endpoint")]
        [HttpPost("fake")]
        public void FakeAction() { }
    }

    private sealed class FakeDocumentsControllerWithoutDescription
    {
        [MenuEndpoint(menuKey: "documents", endpointKey: "documents.missing-desc", description: "")]
        [HttpPost("missing-desc")]
        public void MissingDescriptionAction() { }
    }

    private static ActionDescriptor BuildDescriptor(MethodInfo method)
    {
        return new ControllerActionDescriptor
        {
            ControllerName = method.DeclaringType!.Name,
            ActionName = method.Name,
            MethodInfo = method,
            EndpointMetadata = method.GetCustomAttributes().ToArray(),
        };
    }

    [Fact]
    public void Scan_WithAttributeOnAction_RegistersDescriptor()
    {
        var method = typeof(FakeDocumentsController).GetMethod(nameof(FakeDocumentsController.FakeAction))!;
        var registry = new MenuEndpointRegistry([BuildDescriptor(method)]);

        var found = registry.Find("documents.fake");
        Assert.NotNull(found);
        Assert.Equal("documents", found!.MenuKey);
        Assert.Equal("documents.fake", found.EndpointKey);
        Assert.Equal("测试 endpoint", found.Description);
        Assert.Equal("POST", found.HttpMethod);
        Assert.Equal("fake", found.Path);
    }

    [Fact]
    public void Find_WithUnknownKey_ReturnsNull()
    {
        var method = typeof(FakeDocumentsController).GetMethod(nameof(FakeDocumentsController.FakeAction))!;
        var registry = new MenuEndpointRegistry([BuildDescriptor(method)]);

        Assert.Null(registry.Find("documents.unknown"));
    }

    [Fact]
    public void Scan_WithEmptyDescription_Throws()
    {
        var method = typeof(FakeDocumentsControllerWithoutDescription).GetMethod(nameof(FakeDocumentsControllerWithoutDescription.MissingDescriptionAction))!;

        var error = Assert.Throws<InvalidOperationException>(() =>
            new MenuEndpointRegistry([BuildDescriptor(method)]));
        Assert.Contains("documents.missing-desc", error.Message);
    }

    [Fact]
    public void All_ReturnsRegisteredDescriptors()
    {
        var method = typeof(FakeDocumentsController).GetMethod(nameof(FakeDocumentsController.FakeAction))!;
        var registry = new MenuEndpointRegistry([BuildDescriptor(method)]);

        var all = registry.All;
        Assert.Single(all);
        Assert.Equal("documents.fake", all[0].EndpointKey);
    }
}