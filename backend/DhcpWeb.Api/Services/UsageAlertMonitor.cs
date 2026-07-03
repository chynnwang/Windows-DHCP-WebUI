using DhcpWeb.Api.Data;
using DhcpWeb.Api.Transport;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Services;

/// <summary>后台巡检:定时检查各在线服务器作用域使用率,达到阈值时推送飞书告警(带去重)。</summary>
public class UsageAlertMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDhcpTransport _transport;
    private readonly ILogger<UsageAlertMonitor> _logger;

    // 已告警的作用域 key(serverId:scopeId)。跌回阈值以下时移除,避免每轮重复推送。
    private readonly HashSet<string> _alerted = new();

    public UsageAlertMonitor(IServiceScopeFactory scopeFactory, IDhcpTransport transport,
        ILogger<UsageAlertMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _transport = transport;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动稍作延迟,等 Agent 连上来
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = 10;
            try
            {
                intervalMinutes = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用率告警巡检异常");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>执行一轮巡检,返回下次巡检间隔(分钟)。</summary>
    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cfg = await db.AlertConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.WebhookUrl))
            return cfg?.IntervalMinutes ?? 10;

        var dhcp = scope.ServiceProvider.GetRequiredService<DhcpService>();
        var sender = scope.ServiceProvider.GetRequiredService<FeishuAlertSender>();

        var agents = await db.Agents.ToListAsync(ct);
        var siteNames = await db.Sites.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        foreach (var agent in agents)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!await _transport.IsOnlineAsync(agent.Id)) continue;

                var siteName = agent.SiteId is int sid && siteNames.TryGetValue(sid, out var sn) ? sn : null;
                var scopes = await dhcp.GetScopesAsync(agent.Id, ct);

                foreach (var s in scopes)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var stats = await dhcp.GetScopeStatisticsAsync(agent.Id, s.ScopeId, ct);
                        if (stats is null) continue;
                        var key = $"{agent.Id}:{s.ScopeId}";

                        if (stats.PercentageInUse >= cfg.Threshold)
                        {
                            if (_alerted.Add(key)) // 首次跨过阈值才推送
                            {
                                await sender.SendUsageAlertAsync(cfg.WebhookUrl, agent.Name, siteName,
                                    s.ScopeId, s.Name, stats.PercentageInUse, stats.InUse, stats.Free, ct);
                                _logger.LogInformation("已推送使用率告警 {Key} = {Pct}%", key, stats.PercentageInUse);
                            }
                        }
                        else
                        {
                            _alerted.Remove(key); // 跌回阈值以下,重新武装
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "巡检作用域 {Server}/{Scope} 失败", agent.Id, s.ScopeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "巡检服务器 {Server} 失败", agent.Id);
            }
        }

        return cfg.IntervalMinutes;
    }
}
