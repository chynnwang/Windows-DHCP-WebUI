using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DhcpWeb.Api.Models.Dhcp;

/// <summary>
/// 宽松解析 PowerShell ConvertTo-Json 的值:可能是标量、数组、嵌套数组或数字/布尔,统一规整为 string[]。
/// PS 5.1 对单值会拆成标量、对 DHCP 选项(如租期秒数)输出数字,硬用 string[] 反序列化会失败。
/// </summary>
public class FlexibleStringArrayConverter : JsonConverter<string[]>
{
    // 令 null token 也走 Read,避免属性被置 null(保持空数组)
    public override bool HandleNull => true;

    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        ReadValue(ref reader, list);
        return list.ToArray();
    }

    private static void ReadValue(ref Utf8JsonReader reader, List<string> list)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                break;
            case JsonTokenType.String:
                list.Add(reader.GetString() ?? "");
                break;
            case JsonTokenType.Number:
                list.Add(reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDecimal().ToString(CultureInfo.InvariantCulture));
                break;
            case JsonTokenType.True:
                list.Add("True");
                break;
            case JsonTokenType.False:
                list.Add("False");
                break;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    ReadValue(ref reader, list);
                break;
            case JsonTokenType.StartObject:
                // PS 5.1 对多值集合有时序列化成 { "value": [...], "Count": N },取内层 value
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    if (string.Equals(prop, "value", StringComparison.OrdinalIgnoreCase))
                        ReadValue(ref reader, list);
                    else
                        reader.Skip();
                }
                break;
            default:
                reader.Skip();
                break;
        }
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
