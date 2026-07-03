namespace DhcpWeb.Api.Models.Entities;

/// <summary>已注册的 DHCP 服务器 Agent。Agent 自生成稳定 AgentId,首次连接时自动注册此记录。</summary>
public class Agent
{
    public int Id { get; set; }

    // Agent 自生成并持久化的稳定标识
    public string AgentId { get; set; } = "";

    // 展示名(默认取主机名,管理员可改)
    public string Name { get; set; } = "";

    // 归属工区,null 表示未分组
    public int? SiteId { get; set; }

    public string? Hostname { get; set; }
    public string? DhcpVersion { get; set; }
    public string? AgentVersion { get; set; }
    public string? LastRemoteAddress { get; set; }

    public DateTime? LastSeenUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
