namespace DhcpWeb.Api.Transport;

public interface IDhcpTransport
{
    /// <summary>把 PowerShell 脚本下发给指定服务器对应的 Agent 执行,返回 stdout(通常为 JSON)。</summary>
    Task<string> ExecuteAsync(int serverId, string powershellScript, CancellationToken ct = default);

    /// <summary>该服务器的 Agent 当前是否在线。</summary>
    Task<bool> IsOnlineAsync(int serverId);

    /// <summary>向在线 Agent 下发卸载指令(停止/删除服务、清理配置与 exe)。返回是否成功下发及错误信息。</summary>
    Task<(bool ok, string? error)> TryUninstallAsync(int serverId, CancellationToken ct = default);

    /// <summary>向在线 Agent 下发新的平台回连地址(更新其 config.json 并重启)。返回是否成功下发及错误信息。</summary>
    Task<(bool ok, string? error)> TrySetServerUrlAsync(int serverId, string url, CancellationToken ct = default);
}
