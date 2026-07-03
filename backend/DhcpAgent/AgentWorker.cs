using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;

namespace DhcpAgent;

public class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private HubConnection? _conn;

    public AgentWorker(ILogger<AgentWorker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = AgentConfig.Load();
        if (string.IsNullOrWhiteSpace(cfg.ServerUrl) || string.IsNullOrWhiteSpace(cfg.AgentId))
        {
            _logger.LogError("Agent 未配置(缺少 ServerUrl/AgentId),请先运行 DhcpAgent install。");
            return;
        }

        var hubUrl = $"{cfg.ServerUrl.TrimEnd('/')}/hubs/agent?secret={Uri.EscapeDataString(cfg.EnrollmentSecret)}";
        _conn = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();
        // 与服务端保活参数匹配,容忍反代/网络抖动
        _conn.ServerTimeout = TimeSpan.FromSeconds(60);
        _conn.KeepAliveInterval = TimeSpan.FromSeconds(15);

        // 正常情况下无限重连策略会一直重试;仅当 SignalR 判定错误不可重试而彻底关闭时才会触发本回调,
        // 这里作为兜底再手动拉起连接,保证服务长跑不掉线(服务停止时不再重连)。
        _conn.Closed += async ex =>
        {
            if (stoppingToken.IsCancellationRequested) return;
            _logger.LogWarning("连接已彻底断开: {Msg},将手动重连。", ex?.Message ?? "(无异常)");
            await ConnectWithRetryAsync(cfg, stoppingToken);
        };

        // 平台下发脚本执行(SignalR client results:处理器返回值即为结果)
        _conn.On<string, AgentExecResult>("Execute", async script =>
        {
            _logger.LogInformation("执行脚本: {Preview}", script.Length > 80 ? script[..80] + "..." : script);
            return await PowerShellRunner.RunAsync(script, stoppingToken);
        });

        // 平台下发卸载指令:派生一个独立进程延时后停止并删除服务、清理配置与自身 exe,随即回执。
        _conn.On<AgentExecResult>("Uninstall", () =>
        {
            _logger.LogWarning("收到平台卸载指令,开始自卸载。");
            try
            {
                SelfUninstall();
                return Task.FromResult(new AgentExecResult(true, "uninstall scheduled", null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自卸载启动失败");
                return Task.FromResult(new AgentExecResult(false, null, ex.Message));
            }
        });

        // 平台下发改址指令:更新 config.json 的回连地址,派生独立进程重启本服务,重启后按新地址连接。
        _conn.On<string, AgentExecResult>("SetServerUrl", newUrl =>
        {
            _logger.LogWarning("收到平台改址指令: {Url},将更新配置并重启。", newUrl);
            try
            {
                ScheduleServerUrlChange(newUrl);
                return Task.FromResult(new AgentExecResult(true, "server url updated, restarting", null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "改址失败");
                return Task.FromResult(new AgentExecResult(false, null, ex.Message));
            }
        });

        _conn.Reconnected += async _ =>
        {
            _logger.LogInformation("已重连,重新注册。");
            await RegisterAsync(cfg);
        };

        // 首次连接:失败则退避重试,直到成功或服务停止
        await ConnectWithRetryAsync(cfg, stoppingToken);

        // 保持运行直到服务停止
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* 正常停止 */ }
    }

    // 建立连接并注册,失败则固定退避重试,直到成功或服务停止。
    private async Task ConnectWithRetryAsync(AgentConfig cfg, CancellationToken stoppingToken)
    {
        if (_conn == null) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _conn.StartAsync(stoppingToken);
                await RegisterAsync(cfg);
                _logger.LogInformation("已连接平台 {Url} 并注册,AgentId={AgentId}", cfg.ServerUrl, cfg.AgentId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("连接平台失败: {Msg},10 秒后重试。", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // 生成一个自删除批处理并以独立进程启动:先延时(让本次回执返回),再停止/删除服务、
    // 清理配置目录与自身 exe。服务停止后本进程退出释放 exe 锁,批处理即可删除 exe。
    private void SelfUninstall()
    {
        var exe = Environment.ProcessPath ?? "";
        var cfgDir = AgentConfig.ConfigDir;
        var bat = Path.Combine(Path.GetTempPath(), $"dhcpagent-uninstall-{Guid.NewGuid():N}.cmd");
        var script =
            "@echo off\r\n" +
            "ping 127.0.0.1 -n 6 >nul\r\n" +
            $"sc stop {Installer.ServiceName} >nul 2>&1\r\n" +
            "ping 127.0.0.1 -n 3 >nul\r\n" +
            $"sc delete {Installer.ServiceName} >nul 2>&1\r\n" +
            $"rmdir /s /q \"{cfgDir}\" >nul 2>&1\r\n" +
            (string.IsNullOrEmpty(exe) ? "" : $"del /f /q \"{exe}\" >nul 2>&1\r\n") +
            "(goto) 2>nul & del /f /q \"%~f0\"\r\n";
        File.WriteAllText(bat, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    // 更新回连地址后重启服务:写入新 ServerUrl,再派生独立 cmd 延时 sc stop/start(让本次回执先返回)。
    // 服务重启后 ExecuteAsync 会读取新配置并连接到新地址;无限重连策略保证新地址暂不可达时持续重试。
    private static void ScheduleServerUrlChange(string newUrl)
    {
        var url = (newUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("回连地址为空");

        var cfg = AgentConfig.Load();
        cfg.ServerUrl = url;
        cfg.Save();

        var bat = Path.Combine(Path.GetTempPath(), $"dhcpagent-seturl-{Guid.NewGuid():N}.cmd");
        var script =
            "@echo off\r\n" +
            "ping 127.0.0.1 -n 4 >nul\r\n" +
            $"sc stop {Installer.ServiceName} >nul 2>&1\r\n" +
            "ping 127.0.0.1 -n 3 >nul\r\n" +
            $"sc start {Installer.ServiceName} >nul 2>&1\r\n" +
            "(goto) 2>nul & del /f /q \"%~f0\"\r\n";
        File.WriteAllText(bat, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private async Task RegisterAsync(AgentConfig cfg)
    {
        if (_conn == null) return;
        var dhcpVersion = await GetDhcpVersionAsync();
        var agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        await _conn.InvokeAsync("Register", cfg.AgentId, Environment.MachineName, dhcpVersion, agentVersion);
    }

    private static async Task<string?> GetDhcpVersionAsync()
    {
        var r = await PowerShellRunner.RunAsync(
            "try { $v = Get-DhcpServerVersion; \"$($v.MajorVersion).$($v.MinorVersion)\" } catch { '' }");
        var v = r.Stdout?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_conn != null) await _conn.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}

// 永不放弃的重连策略:前几次快速退避,之后固定 30 秒重试,保证 DHCP 服务器长期在线纳管。
internal sealed class InfiniteRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] _schedule =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    };

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var i = retryContext.PreviousRetryCount;
        return i < _schedule.Length ? _schedule[i] : TimeSpan.FromSeconds(30);
    }
}
