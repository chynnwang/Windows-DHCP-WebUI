using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Transport;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Services;

/// <summary>后台采集:定时拉取各在线服务器的租约,和上一轮快照对比,发现新客户端获取地址即写一条租约日志。</summary>
public class LeaseLogCollector : BackgroundService
{
    private const int IntervalMinutes = 5;   // 巡检间隔
    private const int RetentionDays = 30;     // 日志保留天数

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDhcpTransport _transport;
    private readonly ILogger<LeaseLogCollector> _logger;

    // 每个作用域已知的租约 key(clientId|ip)。首轮建立基线(不记日志),之后仅记新增。
    // 进程内维护,重启后重新建立基线,避免把已有租约当成"新获取"批量刷屏。
    private readonly Dictionary<string, HashSet<string>> _known = new();

    public LeaseLogCollector(IServiceScopeFactory scopeFactory, IDhcpTransport transport,
        ILogger<LeaseLogCollector> logger)
    {
        _scopeFactory = scopeFactory;
        _transport = transport;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "租约日志采集异常"); }

            try { await Task.Delay(TimeSpan.FromMinutes(IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dhcp = scope.ServiceProvider.GetRequiredService<DhcpService>();

        var agents = await db.Agents.ToListAsync(ct);
        var newLogs = new List<LeaseLog>();

        foreach (var agent in agents)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!await _transport.IsOnlineAsync(agent.Id)) continue;

                var scopes = await dhcp.GetScopesAsync(agent.Id, ct);
                foreach (var s in scopes)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var leases = await dhcp.GetLeasesAsync(agent.Id, s.ScopeId, ct);
                        var mapKey = $"{agent.Id}:{s.ScopeId}";
                        var current = leases
                            .Where(l => !string.IsNullOrWhiteSpace(l.IPAddress))
                            .Select(l => $"{l.ClientId ?? ""}|{l.IPAddress}")
                            .ToHashSet();

                        if (!_known.TryGetValue(mapKey, out var prev))
                        {
                            // 首轮:建立基线,不记日志
                            _known[mapKey] = current;
                            continue;
                        }

                        foreach (var l in leases)
                        {
                            if (string.IsNullOrWhiteSpace(l.IPAddress)) continue;
                            var key = $"{l.ClientId ?? ""}|{l.IPAddress}";
                            if (prev.Contains(key)) continue;
                            newLogs.Add(new LeaseLog
                            {
                                AgentId = agent.Id,
                                ServerName = agent.Name,
                                ScopeId = s.ScopeId,
                                ScopeName = s.Name,
                                IpAddress = l.IPAddress,
                                ClientId = l.ClientId,
                                HostName = l.HostName,
                                SeenAtUtc = DateTime.UtcNow
                            });
                        }
                        _known[mapKey] = current;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "采集租约 {Server}/{Scope} 失败", agent.Id, s.ScopeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "采集服务器 {Server} 租约失败", agent.Id);
            }
        }

        if (newLogs.Count > 0)
        {
            db.LeaseLogs.AddRange(newLogs);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("记录 {Count} 条新租约日志", newLogs.Count);
        }

        // 清理过期日志
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        await db.LeaseLogs.Where(l => l.SeenAtUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}
