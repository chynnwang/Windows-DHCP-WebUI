namespace DhcpWeb.Api.Models.Entities;

/// <summary>租约日志:后台巡检对比租约快照,发现新客户端获取地址时记一条。</summary>
public class LeaseLog
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public string ServerName { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string? ScopeName { get; set; }
    public string IpAddress { get; set; } = "";
    public string? ClientId { get; set; }
    public string? HostName { get; set; }
    public DateTime SeenAtUtc { get; set; } = DateTime.UtcNow;
}
