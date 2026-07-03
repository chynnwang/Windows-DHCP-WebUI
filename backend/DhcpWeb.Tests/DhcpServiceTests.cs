using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Services;
using DhcpWeb.Api.Transport;

namespace DhcpWeb.Tests;

/// <summary>捕获 DhcpService 拼装出的 PowerShell 脚本,便于断言命令内容。</summary>
internal sealed class CapturingTransport : IDhcpTransport
{
    public string? LastScript { get; private set; }
    public string Response { get; set; } = "";

    public Task<string> ExecuteAsync(int serverId, string powershellScript, CancellationToken ct = default)
    {
        LastScript = powershellScript;
        return Task.FromResult(Response);
    }

    public Task<bool> IsOnlineAsync(int serverId) => Task.FromResult(true);

    public Task<(bool ok, string? error)> TryUninstallAsync(int serverId, CancellationToken ct = default)
        => Task.FromResult((true, (string?)null));

    public Task<(bool ok, string? error)> TrySetServerUrlAsync(int serverId, string url, CancellationToken ct = default)
        => Task.FromResult((true, (string?)null));
}

public class DhcpServiceTests
{
    private static (DhcpService svc, CapturingTransport t) Build(string response = "")
    {
        var t = new CapturingTransport { Response = response };
        return (new DhcpService(t), t);
    }

    // ---------- 写操作:命令拼装 ----------

    [Fact]
    public async Task CreateScope_BuildsExpectedCmdlet()
    {
        var (svc, t) = Build();
        await svc.CreateScopeAsync(1, new CreateScopeRequest(
            Name: "办公网段", StartRange: "192.168.1.100", EndRange: "192.168.1.200",
            SubnetMask: "255.255.255.0", Description: "1F", LeaseDays: 8, Active: true));

        var s = t.LastScript!;
        Assert.Contains("Add-DhcpServerv4Scope", s);
        Assert.Contains("-Name '办公网段'", s);
        Assert.Contains("-StartRange '192.168.1.100'", s);
        Assert.Contains("-EndRange '192.168.1.200'", s);
        Assert.Contains("-SubnetMask '255.255.255.0'", s);
        Assert.Contains("-State Active", s);
        Assert.Contains("-Description '1F'", s);
        Assert.Contains("New-TimeSpan -Days 8", s);
        Assert.Contains("-ErrorAction Stop", s);
    }

    [Fact]
    public async Task CreateScope_Inactive_UsesInActiveState()
    {
        var (svc, t) = Build();
        await svc.CreateScopeAsync(1, new CreateScopeRequest(
            "网段", "10.0.0.10", "10.0.0.20", "255.0.0.0", null, null, Active: false));
        Assert.Contains("-State InActive", t.LastScript!);
        Assert.DoesNotContain("New-TimeSpan", t.LastScript!); // 未提供租期
    }

    [Fact]
    public async Task CreateScope_WithGatewayAndDns_AppendsOptionValues()
    {
        var (svc, t) = Build();
        await svc.CreateScopeAsync(1, new CreateScopeRequest(
            Name: "办公", StartRange: "192.168.1.100", EndRange: "192.168.1.200",
            SubnetMask: "255.255.255.0", Description: null, LeaseDays: null, Active: true,
            Gateway: "192.168.1.1", DnsServers: new[] { "8.8.8.8", "114.114.114.114" }, DnsDomain: "corp.local"));

        var s = t.LastScript!;
        Assert.Contains("-PassThru", s);
        Assert.Contains("-OptionId 3 -Value '192.168.1.1'", s);
        Assert.Contains("-OptionId 6 -Value '8.8.8.8','114.114.114.114'", s);
        Assert.Contains("-OptionId 15 -Value 'corp.local'", s);
        Assert.Contains("-ScopeId $s.ScopeId", s);
    }

    [Fact]
    public async Task AddReservation_NormalizesMacAndBuildsCmdlet()
    {
        var (svc, t) = Build();
        await svc.AddReservationAsync(1, new AddReservationRequest(
            ScopeId: "192.168.1.0", IPAddress: "192.168.1.50",
            ClientId: "00-11-22-33-44-55", Name: "打印机", Description: null));

        var s = t.LastScript!;
        Assert.Contains("Add-DhcpServerv4Reservation", s);
        Assert.Contains("-ScopeId '192.168.1.0'", s);
        Assert.Contains("-IPAddress '192.168.1.50'", s);
        Assert.Contains("-ClientId '001122334455'", s); // MAC 已规整为纯 hex
        Assert.Contains("-Name '打印机'", s);
    }

    [Fact]
    public async Task SetOption_JoinsMultipleIpValues()
    {
        var (svc, t) = Build();
        await svc.SetOptionAsync(1, new SetOptionRequest(
            ScopeId: "192.168.1.0", OptionId: 6, Values: new[] { "192.168.1.1", "8.8.8.8" }));

        var s = t.LastScript!;
        Assert.Contains("Set-DhcpServerv4OptionValue", s);
        Assert.Contains("-ScopeId '192.168.1.0'", s);
        Assert.Contains("-OptionId 6", s);
        Assert.Contains("-Value '192.168.1.1','8.8.8.8'", s);
    }

    [Fact]
    public async Task UpdateScope_OnlyIncludesProvidedFields()
    {
        var (svc, t) = Build();
        await svc.UpdateScopeAsync(1, "192.168.1.0", new UpdateScopeRequest(
            Name: "新名", Description: null, LeaseDays: null, Active: false));

        var s = t.LastScript!;
        Assert.Contains("Set-DhcpServerv4Scope", s);
        Assert.Contains("-ScopeId '192.168.1.0'", s);
        Assert.Contains("-Name '新名'", s);
        Assert.Contains("-State InActive", s);
        Assert.DoesNotContain("-Description", s);
        Assert.DoesNotContain("New-TimeSpan", s);
    }

    // ---------- 命令注入防护:非法输入应抛异常,不触达传输层 ----------

    [Fact]
    public async Task CreateScope_InjectionInName_Throws()
    {
        var (svc, t) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateScopeAsync(1,
            new CreateScopeRequest("evil'; Remove-Item C:\\", "192.168.1.1", "192.168.1.2",
                "255.255.255.0", null, null, true)));
        Assert.Null(t.LastScript); // 未拼装、未下发
    }

    [Fact]
    public async Task Statistics_InjectionInScopeId_Throws()
    {
        var (svc, t) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GetScopeStatisticsAsync(1, "1.2.3.4;evil"));
        Assert.Null(t.LastScript);
    }

    [Fact]
    public async Task SetOption_NonIpValue_Throws()
    {
        var (svc, t) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetOptionAsync(1,
            new SetOptionRequest("192.168.1.0", 6, new[] { "8.8.8.8", "$(calc)" })));
    }

    // ---------- JSON 解析:PS 5.1 兼容 ----------

    [Fact]
    public async Task GetScopes_SingleObjectJson_NormalizedToOneItem()
    {
        // PS 5.1 单个结果返回对象 {} 而非数组
        var single = """
        {"ScopeId":"192.168.1.0","Name":"仅一个","SubnetMask":"255.255.255.0",
         "StartRange":"192.168.1.10","EndRange":"192.168.1.20","State":"Active"}
        """;
        var (svc, _) = Build(single);
        var list = await svc.GetScopesAsync(1);
        Assert.Single(list);
        Assert.Equal("192.168.1.0", list[0].ScopeId);
    }

    [Fact]
    public async Task GetScopes_ArrayJson_ParsesAll()
    {
        var arr = """
        [{"ScopeId":"10.0.0.0","Name":"A","SubnetMask":"255.0.0.0","StartRange":"10.0.0.1","EndRange":"10.0.0.9","State":"Active"},
         {"ScopeId":"10.0.1.0","Name":"B","SubnetMask":"255.0.0.0","StartRange":"10.0.1.1","EndRange":"10.0.1.9","State":"InActive"}]
        """;
        var (svc, _) = Build(arr);
        var list = await svc.GetScopesAsync(1);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetScopes_EmptyResponse_ReturnsEmptyList()
    {
        var (svc, _) = Build("");
        Assert.Empty(await svc.GetScopesAsync(1));
    }

    // ---------- 选项值宽松解析:PS 输出可能是标量/数字/数组/嵌套 ----------

    [Fact]
    public async Task GetOptions_ScalarStringValue_WrappedToArray()
    {
        // 单值时 PS ConvertTo-Json 把 Value 拆成标量字符串
        var json = """
        {"OptionId":15,"Name":"DNS Domain Name","Value":"corp.local"}
        """;
        var (svc, _) = Build(json);
        var list = await svc.GetOptionsAsync(1, "192.168.1.0");
        Assert.Single(list);
        Assert.Equal(new[] { "corp.local" }, list[0].Value);
    }

    [Fact]
    public async Task GetOptions_NumericValue_ConvertedToString()
    {
        // 如租期(秒)等数字选项
        var json = """
        {"OptionId":51,"Name":"Lease","Value":691200}
        """;
        var (svc, _) = Build(json);
        var list = await svc.GetOptionsAsync(1, "192.168.1.0");
        Assert.Equal(new[] { "691200" }, list[0].Value);
    }

    [Fact]
    public async Task GetOptions_ArrayAndNestedArray_Flattened()
    {
        var json = """
        [{"OptionId":6,"Name":"DNS","Value":["192.168.1.1","8.8.8.8"]},
         {"OptionId":3,"Name":"Router","Value":[["10.0.0.1"]]}]
        """;
        var (svc, _) = Build(json);
        var list = await svc.GetOptionsAsync(1, null);
        Assert.Equal(new[] { "192.168.1.1", "8.8.8.8" }, list[0].Value);
        Assert.Equal(new[] { "10.0.0.1" }, list[1].Value); // 嵌套数组被拍平
    }

    [Fact]
    public async Task GetOptions_PsCollectionObjectShape_ExtractsInnerValue()
    {
        // PS 5.1 对多值选项有时序列化成 { "value": [...], "Count": N }
        var json = """
        {"OptionId":6,"Name":"DNS","Value":{"value":["192.168.12.10","192.168.12.11"],"Count":2}}
        """;
        var (svc, _) = Build(json);
        var list = await svc.GetOptionsAsync(1, "192.168.1.0");
        Assert.Equal(new[] { "192.168.12.10", "192.168.12.11" }, list[0].Value);
    }

    [Fact]
    public async Task SetOption_DomainNameValue_Allowed()
    {
        var (svc, t) = Build();
        await svc.SetOptionAsync(1, new SetOptionRequest("192.168.1.0", 15, new[] { "corp.local" }));
        Assert.Contains("-Value 'corp.local'", t.LastScript!);
    }

    [Fact]
    public async Task RemoveOption_BuildsCmdlet()
    {
        var (svc, t) = Build();
        await svc.RemoveOptionAsync(1, 6, "192.168.1.0");
        var s = t.LastScript!;
        Assert.Contains("Remove-DhcpServerv4OptionValue", s);
        Assert.Contains("-ScopeId '192.168.1.0'", s);
        Assert.Contains("-OptionId 6", s);
    }

    [Fact]
    public async Task GetOptions_NullValue_EmptyArray()
    {
        var json = """
        {"OptionId":6,"Name":"DNS","Value":null}
        """;
        var (svc, _) = Build(json);
        Assert.Empty(list_Value(await svc.GetOptionsAsync(1, "192.168.1.0")));

        static string[] list_Value(List<DhcpWeb.Api.Models.Dhcp.DhcpOptionValue> l) => l[0].Value;
    }
}
