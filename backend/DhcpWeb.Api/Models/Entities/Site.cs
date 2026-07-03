namespace DhcpWeb.Api.Models.Entities;

/// <summary>工区:对已连接 DHCP 服务器的组织分组。服务器通过 Agent.SiteId 归属工区,未分组为 null。</summary>
public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
