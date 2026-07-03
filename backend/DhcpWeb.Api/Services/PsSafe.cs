using System.Net;
using System.Text.RegularExpressions;

namespace DhcpWeb.Api.Services;

/// <summary>
/// PowerShell 命令拼装的输入校验与转义。所有来自用户的值在拼进脚本前必须经过这里,
/// 防止命令注入。校验失败抛 ArgumentException。
/// </summary>
public static class PsSafe
{
    private static readonly Regex NameRx = new(@"^[\w一-龥 .\-_]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex MacRx = new(@"^([0-9A-Fa-f]{2}[:\-]?){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

    public static string Ip(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException($"无效的 IPv4 地址: {value}");
        return ip.ToString();
    }

    public static string Mac(string value)
    {
        if (!MacRx.IsMatch(value))
            throw new ArgumentException($"无效的 MAC 地址: {value}");
        // 规整为纯 hex(DHCP ClientId 常用无分隔或短横线)
        return Regex.Replace(value, "[:\\-]", "");
    }

    /// <summary>校验名称/描述等自由文本,只允许安全字符集。</summary>
    public static string Name(string value, bool allowEmpty = false)
    {
        value ??= "";
        if (value.Length == 0)
        {
            if (allowEmpty) return "";
            throw new ArgumentException("名称不能为空");
        }
        if (!NameRx.IsMatch(value))
            throw new ArgumentException($"名称含非法字符: {value}");
        return value;
    }

    private static readonly Regex OptionValueRx = new(@"^[\w一-龥.\-_ ]{1,253}$", RegexOptions.Compiled);

    /// <summary>校验 DHCP 选项值:IP 优先(标准化),否则按安全字符集(数字/域名/主机名)校验。</summary>
    public static string OptionValue(string value)
    {
        value ??= "";
        if (value.Length == 0)
            throw new ArgumentException("选项值不能为空");
        if (IPAddress.TryParse(value, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return ip.ToString();
        if (!OptionValueRx.IsMatch(value))
            throw new ArgumentException($"选项值含非法字符: {value}");
        return value;
    }

    public static int Int(int value, int min, int max)
    {
        if (value < min || value > max)
            throw new ArgumentException($"数值 {value} 超出范围 [{min},{max}]");
        return value;
    }

    /// <summary>用单引号包裹并转义,作为 PowerShell 字符串字面量。仅用于已校验的值,双保险。</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
