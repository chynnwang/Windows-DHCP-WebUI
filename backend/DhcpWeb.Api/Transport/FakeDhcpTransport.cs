namespace DhcpWeb.Api.Transport;

/// <summary>本机无 Windows/Agent 环境时返回样例 JSON,用于跑通 UI 与联调。按脚本内容粗略分派。</summary>
public class FakeDhcpTransport : IDhcpTransport
{
    public Task<bool> IsOnlineAsync(int serverId) => Task.FromResult(true);

    public Task<(bool ok, string? error)> TryUninstallAsync(int serverId, CancellationToken ct = default)
        => Task.FromResult((true, (string?)null));

    public Task<(bool ok, string? error)> TrySetServerUrlAsync(int serverId, string url, CancellationToken ct = default)
        => Task.FromResult((true, (string?)null));

    public Task<string> ExecuteAsync(int serverId, string script, CancellationToken ct = default)
    {
        if (script.Contains("Get-DhcpServerVersion"))
            return Task.FromResult("""{"MajorVersion":"10","MinorVersion":"0"}""");
        if (script.Contains("Get-DhcpServerv4Scope") && !script.Contains("Statistics"))
            return Task.FromResult("""
            [
              {"ScopeId":"192.168.10.0","Name":"办公网段","Description":"1F 办公","SubnetMask":"255.255.255.0","StartRange":"192.168.10.100","EndRange":"192.168.10.200","State":"Active","LeaseDuration":"8.00:00:00"},
              {"ScopeId":"192.168.20.0","Name":"访客网段","Description":"WiFi","SubnetMask":"255.255.255.0","StartRange":"192.168.20.50","EndRange":"192.168.20.150","State":"Active","LeaseDuration":"1.00:00:00"}
            ]
            """);
        if (script.Contains("Get-DhcpServerv4ScopeStatistics"))
            return Task.FromResult("""{"ScopeId":"192.168.10.0","InUse":42,"Free":59,"PercentageInUse":41.6,"Reserved":5}""");
        if (script.Contains("Get-DhcpServerv4Lease"))
            return Task.FromResult("""
            [
              {"IPAddress":"192.168.10.101","ScopeId":"192.168.10.0","ClientId":"00-11-22-33-44-55","HostName":"PC-001","AddressState":"Active","LeaseExpiryTime":"2026-07-02T10:00:00"},
              {"IPAddress":"192.168.10.102","ScopeId":"192.168.10.0","ClientId":"aa-bb-cc-dd-ee-ff","HostName":"PC-002","AddressState":"Active","LeaseExpiryTime":"2026-07-02T12:30:00"}
            ]
            """);
        if (script.Contains("Get-DhcpServerv4Reservation"))
            return Task.FromResult("""
            [
              {"IPAddress":"192.168.10.10","ScopeId":"192.168.10.0","ClientId":"001122334455","Name":"打印机","Description":"财务打印机","Type":"Both"}
            ]
            """);
        if (script.Contains("Get-DhcpServerv4OptionValue"))
            return Task.FromResult("""
            [
              {"OptionId":3,"Name":"Router","Value":["192.168.10.1"]},
              {"OptionId":6,"Name":"DNS Servers","Value":["192.168.10.1","8.8.8.8"]}
            ]
            """);
        // 写操作:返回空表示成功
        return Task.FromResult("");
    }
}
