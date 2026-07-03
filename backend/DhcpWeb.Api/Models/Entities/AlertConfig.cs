namespace DhcpWeb.Api.Models.Entities;

/// <summary>使用率告警配置(单行,Id=1)。作用域地址使用率达到阈值时推送飞书机器人卡片。</summary>
public class AlertConfig
{
    public int Id { get; set; }

    // 是否启用后台巡检推送
    public bool Enabled { get; set; }

    // 飞书自定义机器人 webhook 地址
    public string WebhookUrl { get; set; } = "";

    // 使用率阈值(百分比),达到或超过即告警
    public double Threshold { get; set; } = 95;

    // 巡检间隔(分钟)
    public int IntervalMinutes { get; set; } = 10;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
