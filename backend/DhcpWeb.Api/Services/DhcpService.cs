using System.Text.Json;
using System.Collections.Concurrent;
using DhcpWeb.Api.Models.Dhcp;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Transport;

namespace DhcpWeb.Api.Services;

public class DhcpService
{
    private readonly IDhcpTransport _transport;
    private static readonly ConcurrentDictionary<string, CacheEntry> ReadCache = new();
    private static readonly TimeSpan ScopeTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public DhcpService(IDhcpTransport transport)
    {
        _transport = transport;
    }

    // ---- 连通性/健康:通过 Agent 跑 Get-DhcpServerVersion ----
    public async Task<DhcpServerInfo?> GetServerInfoAsync(int serverId, CancellationToken ct = default)
    {
        const string script = "Get-DhcpServerVersion | Select-Object MajorVersion, MinorVersion | ConvertTo-Json -Depth 2";
        var list = await RunListAsync<DhcpServerInfo>(serverId, script, ct);
        return list.FirstOrDefault();
    }

    // ---------------- 只读 ----------------

    public async Task<List<DhcpScope>> GetScopesAsync(int serverId, CancellationToken ct = default)
    {
        return await CacheAsync($"server:{serverId}:scopes", ScopeTtl, async () =>
        {
        const string script = """
        Get-DhcpServerv4Scope | Select-Object `
          @{n='ScopeId';e={$_.ScopeId.IPAddressToString}}, Name, Description, `
          @{n='SubnetMask';e={$_.SubnetMask.IPAddressToString}}, `
          @{n='StartRange';e={$_.StartRange.IPAddressToString}}, `
          @{n='EndRange';e={$_.EndRange.IPAddressToString}}, `
          @{n='State';e={$_.State.ToString()}}, `
          @{n='LeaseDuration';e={$_.LeaseDuration.ToString()}} | ConvertTo-Json -Depth 4
        """;
            return await RunListAsync<DhcpScope>(serverId, script, ct);
        });
    }

    public async Task<List<DhcpScopeStatistics>> GetAllScopeStatisticsAsync(int serverId, CancellationToken ct = default)
    {
        return await CacheAsync($"server:{serverId}:scope-statistics:all", ScopeTtl, async () =>
        {
            const string script = """
            Get-DhcpServerv4ScopeStatistics | Select-Object `
              @{n='ScopeId';e={$_.ScopeId.IPAddressToString}}, InUse, Free, PercentageInUse, Reserved | ConvertTo-Json -Depth 3
            """;
            return await RunListAsync<DhcpScopeStatistics>(serverId, script, ct);
        });
    }

    public async Task<DhcpScopeStatistics?> GetScopeStatisticsAsync(int serverId, string scopeId, CancellationToken ct = default)
    {
        var safeScopeId = PsSafe.Ip(scopeId);
        var all = await GetAllScopeStatisticsAsync(serverId, ct);
        var cached = all.FirstOrDefault(s => string.Equals(s.ScopeId, safeScopeId, StringComparison.OrdinalIgnoreCase));
        if (cached != null) return cached;

        var sid = safeScopeId;
        var script = """
        Get-DhcpServerv4ScopeStatistics -ScopeId '__SID__' | Select-Object `
          @{n='ScopeId';e={$_.ScopeId.IPAddressToString}}, InUse, Free, PercentageInUse, Reserved | ConvertTo-Json -Depth 3
        """.Replace("__SID__", sid);
        var list = await RunListAsync<DhcpScopeStatistics>(serverId, script, ct);
        return list.FirstOrDefault();
    }

    public async Task<List<DhcpLease>> GetLeasesAsync(int serverId, string scopeId, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(scopeId);
        return await CacheAsync($"server:{serverId}:scope:{sid}:leases", DetailTtl, async () =>
        {
        var script = """
        Get-DhcpServerv4Lease -ScopeId '__SID__' | Select-Object `
          @{n='IPAddress';e={$_.IPAddress.IPAddressToString}}, `
          @{n='ScopeId';e={$_.ScopeId.IPAddressToString}}, ClientId, HostName, `
          @{n='AddressState';e={$_.AddressState.ToString()}}, `
          @{n='LeaseExpiryTime';e={if($_.LeaseExpiryTime){$_.LeaseExpiryTime.ToString('o')}else{$null}}} | ConvertTo-Json -Depth 4
        """.Replace("__SID__", sid);
            return await RunListAsync<DhcpLease>(serverId, script, ct);
        });
    }

    public async Task<List<DhcpReservation>> GetReservationsAsync(int serverId, string scopeId, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(scopeId);
        return await CacheAsync($"server:{serverId}:scope:{sid}:reservations", DetailTtl, async () =>
        {
        var script = """
        Get-DhcpServerv4Reservation -ScopeId '__SID__' | Select-Object `
          @{n='IPAddress';e={$_.IPAddress.IPAddressToString}}, `
          @{n='ScopeId';e={$_.ScopeId.IPAddressToString}}, ClientId, Name, Description, `
          @{n='Type';e={$_.Type.ToString()}} | ConvertTo-Json -Depth 4
        """.Replace("__SID__", sid);
            return await RunListAsync<DhcpReservation>(serverId, script, ct);
        });
    }

    public async Task<List<DhcpOptionValue>> GetOptionsAsync(int serverId, string? scopeId, CancellationToken ct = default)
    {
        var cacheScope = string.IsNullOrEmpty(scopeId) ? "server" : PsSafe.Ip(scopeId);
        return await CacheAsync($"server:{serverId}:options:{cacheScope}", DetailTtl, async () =>
        {
        string scopeArg = string.IsNullOrEmpty(scopeId)
            ? ""
            : $" -ScopeId '{PsSafe.Ip(scopeId)}'";
        var script = "Get-DhcpServerv4OptionValue__SCOPEARG__ | Select-Object OptionId, Name, Value | ConvertTo-Json -Depth 6"
            .Replace("__SCOPEARG__", scopeArg);
            return await RunListAsync<DhcpOptionValue>(serverId, script, ct);
        });
    }

    // ---------------- 写操作 ----------------

    public async Task CreateScopeAsync(int serverId, CreateScopeRequest r, CancellationToken ct = default)
    {
        var name = PsSafe.Name(r.Name);
        var start = PsSafe.Ip(r.StartRange);
        var end = PsSafe.Ip(r.EndRange);
        var mask = PsSafe.Ip(r.SubnetMask);
        var state = r.Active ? "Active" : "InActive";
        var desc = string.IsNullOrEmpty(r.Description) ? "" : PsSafe.Name(r.Description, allowEmpty: true);
        var descArg = string.IsNullOrEmpty(desc) ? "" : $" -Description {PsSafe.Quote(desc)}";
        var leaseArg = r.LeaseDays is int d
            ? $" -LeaseDuration (New-TimeSpan -Days {PsSafe.Int(d, 1, 3650)})"
            : "";
        var sb = new System.Text.StringBuilder();
        sb.Append($"$s = Add-DhcpServerv4Scope -Name {PsSafe.Quote(name)} -StartRange {PsSafe.Quote(start)} ");
        sb.Append($"-EndRange {PsSafe.Quote(end)} -SubnetMask {PsSafe.Quote(mask)} -State {state}{descArg}{leaseArg} -PassThru -ErrorAction Stop; ");

        // Add 失败时 $s 为 null,用 if ($s) 守卫,避免向 Set 传入空 ScopeId 产生二次噪声错误。
        if (!string.IsNullOrWhiteSpace(r.Gateway))
        {
            var gw = PsSafe.Quote(PsSafe.Ip(r.Gateway));
            sb.Append($"if ($s) {{ Set-DhcpServerv4OptionValue -ScopeId $s.ScopeId -OptionId 3 -Value {gw} -ErrorAction Stop }}; ");
        }
        var dns = (r.DnsServers ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        if (dns.Length > 0)
        {
            var quoted = string.Join(",", dns.Select(v => PsSafe.Quote(PsSafe.Ip(v))));
            sb.Append($"if ($s) {{ Set-DhcpServerv4OptionValue -ScopeId $s.ScopeId -OptionId 6 -Value {quoted} -ErrorAction Stop }}; ");
        }
        if (!string.IsNullOrWhiteSpace(r.DnsDomain))
        {
            var dom = PsSafe.Quote(PsSafe.OptionValue(r.DnsDomain));
            sb.Append($"if ($s) {{ Set-DhcpServerv4OptionValue -ScopeId $s.ScopeId -OptionId 15 -Value {dom} -ErrorAction Stop }}; ");
        }
        await RunWriteAsync(serverId, sb.ToString(), ct);
        ClearServerCache(serverId);
    }

    public async Task UpdateScopeAsync(int serverId, string scopeId, UpdateScopeRequest r, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(scopeId);
        var args = new List<string> { $"-ScopeId {PsSafe.Quote(sid)}" };
        if (!string.IsNullOrEmpty(r.Name)) args.Add($"-Name {PsSafe.Quote(PsSafe.Name(r.Name))}");
        if (r.Description != null) args.Add($"-Description {PsSafe.Quote(PsSafe.Name(r.Description, allowEmpty: true))}");
        if (r.LeaseDays is int d) args.Add($"-LeaseDuration (New-TimeSpan -Days {PsSafe.Int(d, 1, 3650)})");
        if (r.Active is bool a) args.Add($"-State {(a ? "Active" : "InActive")}");
        var script = $"Set-DhcpServerv4Scope {string.Join(' ', args)} -ErrorAction Stop";
        await RunWriteAsync(serverId, script, ct);
        ClearServerCache(serverId);
    }

    public async Task DeleteScopeAsync(int serverId, string scopeId, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(scopeId);
        await RunWriteAsync(serverId, $"Remove-DhcpServerv4Scope -ScopeId {PsSafe.Quote(sid)} -Force -ErrorAction Stop", ct);
        ClearServerCache(serverId);
    }

    public async Task AddReservationAsync(int serverId, AddReservationRequest r, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(r.ScopeId);
        var ip = PsSafe.Ip(r.IPAddress);
        var mac = PsSafe.Mac(r.ClientId);
        var nameArg = string.IsNullOrEmpty(r.Name) ? "" : $" -Name {PsSafe.Quote(PsSafe.Name(r.Name))}";
        var descArg = string.IsNullOrEmpty(r.Description) ? "" : $" -Description {PsSafe.Quote(PsSafe.Name(r.Description, allowEmpty: true))}";
        var script = $"Add-DhcpServerv4Reservation -ScopeId {PsSafe.Quote(sid)} -IPAddress {PsSafe.Quote(ip)} " +
                     $"-ClientId {PsSafe.Quote(mac)}{nameArg}{descArg} -ErrorAction Stop";
        await RunWriteAsync(serverId, script, ct);
        ClearServerCache(serverId);
    }

    public async Task RemoveReservationAsync(int serverId, string scopeId, string ipAddress, CancellationToken ct = default)
    {
        var sid = PsSafe.Ip(scopeId);
        var ip = PsSafe.Ip(ipAddress);
        await RunWriteAsync(serverId,
            $"Remove-DhcpServerv4Reservation -ScopeId {PsSafe.Quote(sid)} -IPAddress {PsSafe.Quote(ip)} -ErrorAction Stop", ct);
        ClearServerCache(serverId);
    }

    public async Task SetOptionAsync(int serverId, SetOptionRequest r, CancellationToken ct = default)
    {
        var optId = PsSafe.Int(r.OptionId, 1, 255);
        var scopeArg = string.IsNullOrEmpty(r.ScopeId) ? "" : $" -ScopeId {PsSafe.Quote(PsSafe.Ip(r.ScopeId))}";
        if (r.Values.Length == 0) throw new ArgumentException("选项值不能为空");
        // 兼容 IP(网关/DNS/WINS)、数字(租约秒)、域名字符串(DNS 域名)等选项
        var quoted = string.Join(",", r.Values.Select(v => PsSafe.Quote(PsSafe.OptionValue(v))));
        var script = $"Set-DhcpServerv4OptionValue{scopeArg} -OptionId {optId} -Value {quoted} -ErrorAction Stop";
        await RunWriteAsync(serverId, script, ct);
        ClearServerCache(serverId);
    }

    public async Task RemoveOptionAsync(int serverId, int optionId, string? scopeId, CancellationToken ct = default)
    {
        var optId = PsSafe.Int(optionId, 1, 255);
        var scopeArg = string.IsNullOrEmpty(scopeId) ? "" : $" -ScopeId {PsSafe.Quote(PsSafe.Ip(scopeId))}";
        await RunWriteAsync(serverId,
            $"Remove-DhcpServerv4OptionValue{scopeArg} -OptionId {optId} -Force -ErrorAction Stop", ct);
        ClearServerCache(serverId);
    }

    // ---------------- 内部 ----------------

    private async Task<List<T>> RunListAsync<T>(int serverId, string script, CancellationToken ct)
    {
        var raw = await _transport.ExecuteAsync(serverId, script, ct);
        return ParseJsonArray<T>(raw);
    }

    private async Task RunWriteAsync(int serverId, string script, CancellationToken ct)
    {
        await _transport.ExecuteAsync(serverId, script, ct);
    }

    private static async Task<T> CacheAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var now = DateTimeOffset.UtcNow;
        if (ReadCache.TryGetValue(key, out var hit) && hit.ExpiresAt > now && hit.Value is T value)
            return value;

        var fresh = await factory();
        ReadCache[key] = new CacheEntry(fresh!, now.Add(ttl));
        return fresh;
    }

    private static void ClearServerCache(int serverId)
    {
        var prefix = $"server:{serverId}:";
        foreach (var key in ReadCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            ReadCache.TryRemove(key, out _);
    }

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);

    /// <summary>
    /// 兼容 Windows PowerShell 5.1 的 ConvertTo-Json:单个对象返回 {},多个返回 []。
    /// 统一规整为数组解析。空白返回空列表。
    /// </summary>
    internal static List<T> ParseJsonArray<T>(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<T>();
        var text = raw.Trim();
        if (text.StartsWith("{"))
            text = "[" + text + "]";
        if (!text.StartsWith("["))
            return new List<T>();
        return JsonSerializer.Deserialize<List<T>>(text, JsonOpts) ?? new List<T>();
    }
}
