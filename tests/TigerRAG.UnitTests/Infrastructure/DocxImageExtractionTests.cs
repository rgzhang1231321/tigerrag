using TigerRAG.Infrastructure.Parsing;
using Xunit.Abstractions;

namespace TigerRAG.UnitTests.Infrastructure;

/// <summary>DocxParser 图片提取诊断测试。</summary>
public sealed class DocxImageExtractionTests
{
    private readonly ITestOutputHelper _output;
    private readonly DocxParser _parser = new();

    private const string TestFilePath = @"D:\360Downloads\张荣刚交接单.docx";

    public DocxImageExtractionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Debug_ImageExtraction()
    {
        if (!File.Exists(TestFilePath))
        {
            _output.WriteLine($"文件不存在: {TestFilePath}");
            return;
        }

        await using var stream = File.OpenRead(TestFilePath);
        var result = await _parser.ParseAsync(stream, CancellationToken.None);

        _output.WriteLine($"内容长度: {result.Content.Length}");
        _output.WriteLine($"图片数量: {result.Images.Count}");
        for (var i = 0; i < result.Images.Count; i++)
        {
            var img = result.Images[i];
            _output.WriteLine($"  图片 {i}: {img.Bytes.Length} bytes, Position={img.Position}");
        }
    }
}
