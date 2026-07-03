namespace DhcpWeb.Api.Models.Dhcp;

// 与远端 PowerShell ConvertTo-Json 输出的字段对应,均用字符串接收以避免类型序列化问题
public class DhcpScope
{
    public string ScopeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string SubnetMask { get; set; } = "";
    public string StartRange { get; set; } = "";
    public string EndRange { get; set; } = "";
    public string State { get; set; } = "";
    public string? LeaseDuration { get; set; }
}

public class DhcpScopeStatistics
{
    public string ScopeId { get; set; } = "";
    public int InUse { get; set; }
    public int Free { get; set; }
    public double PercentageInUse { get; set; }
    public int Reserved { get; set; }
}

public class DhcpLease
{
    public string IPAddress { get; set; } = "";
    public string? ScopeId { get; set; }
    public string? ClientId { get; set; }
    public string? HostName { get; set; }
    public string? AddressState { get; set; }
    public string? LeaseExpiryTime { get; set; }
}

public class DhcpReservation
{
    public string IPAddress { get; set; } = "";
    public string? ScopeId { get; set; }
    public string? ClientId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
}

public class DhcpOptionValue
{
    public int OptionId { get; set; }
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(FlexibleStringArrayConverter))]
    public string[] Value { get; set; } = System.Array.Empty<string>();
}

public class DhcpServerInfo
{
    public string? MajorVersion { get; set; }
    public string? MinorVersion { get; set; }
}
