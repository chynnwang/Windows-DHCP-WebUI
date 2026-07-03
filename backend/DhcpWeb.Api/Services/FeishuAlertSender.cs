using System.Text;
using System.Text.Json;

namespace DhcpWeb.Api.Services;

/// <summary>向飞书自定义机器人 webhook 推送交互卡片。</summary>
public class FeishuAlertSender
{
    private readonly HttpClient _http;
    private readonly ILogger<FeishuAlertSender> _logger;

    public FeishuAlertSender(HttpClient http, ILogger<FeishuAlertSender> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>推送一条使用率告警卡片。</summary>
    public Task SendUsageAlertAsync(string webhookUrl, string serverName, string? siteName,
        string scopeId, string scopeName, double percentage, int inUse, int free, CancellationToken ct = default)
    {
        var content =
            $"**服务器**:{serverName}（{siteName ?? "未分组"}）\n" +
            $"**作用域**:{scopeName}（{scopeId}）\n" +
            $"**地址使用率**:<font color='red'>{percentage:0.#}%</font>\n" +
            $"**已用 / 空闲**:{inUse} / {free}\n" +
            $"**时间**:{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var card = BuildCard("🚨 DHCP 地址使用率告警", "red", content);
        return PostAsync(webhookUrl, card, ct);
    }

    /// <summary>发送一条测试卡片,验证 webhook 是否可用。</summary>
    public Task SendTestAsync(string webhookUrl, CancellationToken ct = default)
    {
        var content =
            "这是一条 **DHCP 管理平台** 的测试告警。\n" +
            "如果你收到此卡片,说明飞书机器人 webhook 配置正确。\n" +
            $"**时间**:{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var card = BuildCard("✅ DHCP 告警测试", "blue", content);
        return PostAsync(webhookUrl, card, ct);
    }

    private static object BuildCard(string title, string template, string mdContent) => new
    {
        msg_type = "interactive",
        card = new
        {
            config = new { wide_screen_mode = true },
            header = new
            {
                template,
                title = new { tag = "plain_text", content = title }
            },
            elements = new object[]
            {
                new { tag = "div", text = new { tag = "lark_md", content = mdContent } }
            }
        }
    };

    private async Task PostAsync(string webhookUrl, object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new ArgumentException("未配置飞书机器人 webhook 地址");

        var json = JsonSerializer.Serialize(payload);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(webhookUrl, body, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        // 飞书即使 HTTP 200 也可能在 body 里返回 code!=0(如关键词不匹配、机器人被移除等)
        var code = TryReadCode(respText);
        if (!resp.IsSuccessStatusCode || (code is int c && c != 0))
        {
            _logger.LogWarning("飞书推送失败: status={Status} body={Body}", (int)resp.StatusCode, respText);
            throw new InvalidOperationException($"飞书推送失败: {respText}");
        }
    }

    private static int? TryReadCode(string respText)
    {
        try
        {
            using var doc = JsonDocument.Parse(respText);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c))
                return c;
            // 新版返回 StatusCode 字段
            if (doc.RootElement.TryGetProperty("StatusCode", out var sc) && sc.TryGetInt32(out var c2))
                return c2;
        }
        catch { /* 非 JSON,忽略 */ }
        return null;
    }
}
