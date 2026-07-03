using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using DhcpWeb.Api.Transport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/servers")]
[Authorize]
public class ServersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AgentRegistry _registry;
    private readonly DhcpService _dhcp;
    private readonly IDhcpTransport _transport;
    private readonly AuditService _audit;

    public ServersController(AppDbContext db, AgentRegistry registry, DhcpService dhcp, IDhcpTransport transport, AuditService audit)
    {
        _db = db;
        _registry = registry;
        _dhcp = dhcp;
        _transport = transport;
        _audit = audit;
    }

    private string CurrentUser =>
        User?.Identity?.Name ?? Request.Headers["X-Actor"].FirstOrDefault() ?? "embedded";

    private ServerDto ToDto(Agent a, IReadOnlyDictionary<int, string> siteNames)
    {
        var online = _registry.IsOnline(a.AgentId);
        var lastSeen = online ? DateTime.UtcNow : a.LastSeenUtc;

        return new(a.Id, a.AgentId, a.Name, a.Hostname, a.DhcpVersion, a.AgentVersion,
            online, NormalizeUtc(lastSeen),
            a.SiteId, a.SiteId is int sid && siteNames.TryGetValue(sid, out var n) ? n : null);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value == null) return null;
        return value.Value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private async Task<Dictionary<int, string>> SiteNamesAsync()
        => await _db.Sites.ToDictionaryAsync(s => s.Id, s => s.Name);

    [HttpGet]
    public async Task<ActionResult<List<ServerDto>>> List()
    {
        var names = await SiteNamesAsync();
        return Ok((await _db.Agents.OrderBy(a => a.Name).ToListAsync()).Select(a => ToDto(a, names)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServerDto>> Get(int id)
    {
        var a = await _db.Agents.FindAsync(id);
        return a == null ? NotFound() : ToDto(a, await SiteNamesAsync());
    }

    /// <summary>把服务器归入工区(SiteId=null 表示移出工区)。</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:int}/site")]
    public async Task<ActionResult<ServerDto>> AssignSite(int id, AssignSiteRequest req)
    {
        var a = await _db.Agents.FindAsync(id);
        if (a == null) return NotFound();
        if (req.SiteId is int sid && !await _db.Sites.AnyAsync(s => s.Id == sid))
            return BadRequest(new { message = "工区不存在" });
        a.SiteId = req.SiteId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "AssignSite", $"{a.AgentId}->{req.SiteId}", true);
        return ToDto(a, await SiteNamesAsync());
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServerDto>> Rename(int id, RenameServerRequest req)
    {
        var a = await _db.Agents.FindAsync(id);
        if (a == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "名称不能为空" });
        a.Name = req.Name;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "RenameServer", a.AgentId, true);
        return ToDto(a, await SiteNamesAsync());
    }

    /// <summary>更改 Agent 回连本平台的地址(域名或 IP,可含端口)。Agent 更新配置后自动重启并按新地址连接。</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:int}/callback-url")]
    public async Task<ActionResult> SetCallbackUrl(int id, SetCallbackUrlRequest req, CancellationToken ct)
    {
        var a = await _db.Agents.FindAsync(id);
        if (a == null) return NotFound();

        var url = NormalizeCallbackUrl(req.Url);
        if (url == null)
            return BadRequest(new { message = "地址格式无效,请输入形如 http://主机名或IP:端口 的地址" });
        if (!_registry.IsOnline(a.AgentId))
            return BadRequest(new { message = "Agent 当前离线,无法下发改址指令(需在线时下发)" });

        var (ok, error) = await _transport.TrySetServerUrlAsync(id, url, ct);
        await _audit.LogAsync(CurrentUser, "SetCallbackUrl", $"{a.AgentId}->{url}: {(ok ? "ok" : error)}", ok);
        if (!ok) return StatusCode(502, new { message = $"下发失败:{error}" });
        return Ok(new { message = $"已通知 Agent 切换到 {url},稍后将自动断开并按新地址重连", url });
    }

    // 允许只填主机名/IP(:端口),自动补 http://;仅接受 http/https 绝对地址。
    private static string? NormalizeCallbackUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = "http://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")
            ? s.TrimEnd('/')
            : null;
    }

    /// <summary>删除服务器:Agent 在线时先下发远程卸载(停服/删服务/清配置与 exe),再移除平台记录;离线仅移除记录。</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var a = await _db.Agents.FindAsync(id);
        if (a == null) return NotFound();

        string note;
        if (_registry.IsOnline(a.AgentId))
        {
            var (ok, error) = await _transport.TryUninstallAsync(id, ct);
            note = ok ? "已远程卸载 Agent 并移除记录" : $"记录已移除;远程卸载未成功({error}),请到该服务器手动卸载 Agent";
        }
        else
        {
            note = "Agent 离线,仅移除平台记录;如需彻底清理请到该服务器手动卸载 Agent";
        }

        _db.Agents.Remove(a);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "DeleteServer", $"{a.AgentId}: {note}", true);
        return Ok(new { message = note });
    }

    /// <summary>健康检查:通过 Agent 跑 Get-DhcpServerVersion 确认 DHCP 可用。</summary>
    [HttpPost("{id:int}/health")]
    public async Task<ActionResult> Health(int id, CancellationToken ct)
    {
        var a = await _db.Agents.FindAsync(id);
        if (a == null) return NotFound();
        if (!_registry.IsOnline(a.AgentId))
            return Ok(new { online = false, message = "Agent 离线" });
        try
        {
            var info = await _dhcp.GetServerInfoAsync(id, ct);
            return Ok(new { online = true, dhcpVersion = info == null ? null : $"{info.MajorVersion}.{info.MinorVersion}" });
        }
        catch (Exception ex)
        {
            return Ok(new { online = true, message = $"Agent 在线,但 DHCP 检查失败: {ex.Message}" });
        }
    }
}
