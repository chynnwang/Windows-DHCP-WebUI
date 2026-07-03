namespace DhcpWeb.Api.Models.Entities;

/// <summary>通用键值设置(可在页面运行时修改),如「接入服务器」使用的平台对外地址。</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
