using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Transport;

/// <summary>Agent 通过此 Hub 主动拨出连接。连接时校验注册密钥,连上后调用 Register 自动注册。</summary>
public class AgentHub : Hub
{
    private readonly AgentRegistry _registry;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(AgentRegistry registry, AppDbContext db, IConfiguration config, ILogger<AgentHub> logger)
    {
        _registry = registry;
        _db = db;
        _config = config;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var secret = http?.Request.Query["secret"].ToString();
        // 密钥优先取 Settings 表(可在「接入服务器」弹窗中修改),未设置时回退 appsettings 默认值。
        var overridden = (await _db.Settings.FindAsync(AgentSetup.EnrollmentSecretKey))?.Value;
        var expected = string.IsNullOrWhiteSpace(overridden) ? _config["Agent:EnrollmentSecret"] : overridden;
        if (string.IsNullOrEmpty(expected) || secret != expected)
        {
            _logger.LogWarning("Agent 连接被拒:注册密钥无效 (conn {Conn})", Context.ConnectionId);
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    /// <summary>Agent 连上后调用,上报身份并自动注册/更新记录。</summary>
    public async Task Register(string agentId, string? hostname, string? dhcpVersion, string? agentVersion)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            Context.Abort();
            return;
        }
        var remote = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.AgentId == agentId);
        if (agent == null)
        {
            agent = new Agent
            {
                AgentId = agentId,
                Name = string.IsNullOrWhiteSpace(hostname) ? agentId : hostname!
            };
            _db.Agents.Add(agent);
        }
        agent.Hostname = hostname;
        agent.DhcpVersion = dhcpVersion;
        agent.AgentVersion = agentVersion;
        agent.LastRemoteAddress = remote;
        agent.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _registry.Add(agentId, Context.ConnectionId);
        _logger.LogInformation("Agent 已注册: {AgentId} ({Host}) from {Remote}", agentId, hostname, remote);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.RemoveByConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
