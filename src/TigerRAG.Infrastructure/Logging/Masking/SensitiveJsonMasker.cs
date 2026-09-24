using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// JSON 请求体脱敏器：把 key 命中敏感片段（password/token/secret/authorization）的值替换为 <c>***</c>，
/// 防止密码哈希与令牌进入访问日志。解析失败（含截断的坏 JSON）时退化为正则替换，绝不放行未脱敏密文。
/// </summary>
public static class SensitiveJsonMasker
{
    /// <summary>敏感 key 片段；key 大小写不敏感地包含任一片段即整值替换。</summary>
    private static readonly string[] SensitiveKeyFragments = ["password", "token", "secret", "authorization"];

    /// <summary>脱敏后的占位值。</summary>
    private const string MaskedValue = "***";

    /// <summary>正则兜底用：敏感 key 的字符串值对（"key" : "value"），保留 key 组便于替换。</summary>
    private static readonly Regex SensitivePairRegex = new(
        "(\"[^\"]*(?:password|token|secret|authorization)[^\"]*\"\\s*:\\s*)\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>对 JSON 文本脱敏：敏感 key 的值替换为 <c>***</c>，其余结构原样保留；解析失败走正则兜底。空输入原样返回。</summary>
    public static string? Mask(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        try
        {
            using var document = JsonDocument.Parse(input);
            using var stream = new MemoryStream();
            // 默认编码器会把所有非 ASCII 转义成 \uXXXX，日志里的中文将不可读；
            // 日志不嵌入 HTML，放宽转义没有 XSS 风险。
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                WriteMasked(document.RootElement, writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // 坏 JSON（如超限截断）里已完整的敏感字段仍需脱敏，正则兜底。
            return SensitivePairRegex.Replace(input, "$1\"***\"");
        }
    }

    /// <summary>递归写出脱敏后的 JSON：对象按 key 判定敏感，数组逐项递归，标量原样写出。</summary>
    private static void WriteMasked(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (IsSensitiveKey(property.Name))
                    {
                        // 敏感 key：无论值是什么类型都写占位符，避免任何形式的密文泄漏。
                        writer.WriteString(property.Name, MaskedValue);
                    }
                    else
                    {
                        writer.WritePropertyName(property.Name);
                        WriteMasked(property.Value, writer);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteMasked(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    /// <summary>判断 key 是否大小写不敏感地包含任一敏感片段。</summary>
    private static bool IsSensitiveKey(string key)
    {
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
