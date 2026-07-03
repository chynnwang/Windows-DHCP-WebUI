using DhcpWeb.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Transport;

/// <summary>通过 SignalR 把脚本下发给对应 Agent 执行(client results),返回其 stdout。</summary>
public class AgentDhcpTransport : IDhcpTransport
{
    private readonly IHubContext<AgentHub> _hub;
    private readonly AgentRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;

    public AgentDhcpTransport(IHubContext<AgentHub> hub, AgentRegistry registry, IServiceScopeFactory scopeFactory)
    {
        _hub = hub;
        _registry = registry;
        _scopeFactory = scopeFactory;
    }

    private async Task<string> ResolveAgentIdAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.FindAsync(serverId)
            ?? throw new KeyNotFoundException($"服务器 {serverId} 不存在");
        return agent.AgentId;
    }

    public async Task<bool> IsOnlineAsync(int serverId)
    {
        var agentId = await ResolveAgentIdAsync(serverId);
        return _registry.IsOnline(agentId);
    }

    public async Task<(bool ok, string? error)> TryUninstallAsync(int serverId, CancellationToken ct = default)
    {
        var agentId = await ResolveAgentIdAsync(serverId);
        var connId = _registry.GetConnectionId(agentId);
        if (connId == null) return (false, "Agent 当前离线");
        try
        {
            var result = await _hub.Clients.Client(connId).InvokeAsync<AgentExecResult>("Uninstall", ct);
            return (result.Success, result.Error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool ok, string? error)> TrySetServerUrlAsync(int serverId, string url, CancellationToken ct = default)
    {
        var agentId = await ResolveAgentIdAsync(serverId);
        var connId = _registry.GetConnectionId(agentId);
        if (connId == null) return (false, "Agent 当前离线");
        try
        {
            var result = await _hub.Clients.Client(connId).InvokeAsync<AgentExecResult>("SetServerUrl", url, ct);
            return (result.Success, result.Error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string> ExecuteAsync(int serverId, string powershellScript, CancellationToken ct = default)
    {
        var agentId = await ResolveAgentIdAsync(serverId);
        var connId = _registry.GetConnectionId(agentId)
            ?? throw new InvalidOperationException("Agent 当前离线,无法执行操作");

        // SignalR client results:调用 Agent 端 "Execute" 方法并等待返回
        var result = await _hub.Clients.Client(connId)
            .InvokeAsync<AgentExecResult>("Execute", powershellScript, ct);

        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? "Agent 执行失败" : result.Error);
        return result.Stdout ?? "";
    }
}

/// <summary>Agent 执行 PowerShell 的返回结构。</summary>
public record AgentExecResult(bool Success, string? Stdout, string? Error);
