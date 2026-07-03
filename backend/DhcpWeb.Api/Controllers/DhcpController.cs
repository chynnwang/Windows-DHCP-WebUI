using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId:int}")]
[Authorize]
public class DhcpController : ControllerBase
{
    private readonly DhcpService _dhcp;
    private readonly AuditService _audit;

    public DhcpController(DhcpService dhcp, AuditService audit)
    {
        _dhcp = dhcp;
        _audit = audit;
    }

    // 审计操作者:优先登录用户,其次上层平台传入的 X-Actor 头。
    private string CurrentUser =>
        User?.Identity?.Name ?? Request.Headers["X-Actor"].FirstOrDefault() ?? "embedded";

    private async Task<ActionResult> Guard(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(502, new { message = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { message = $"远程执行失败: {ex.Message}" }); }
    }

    // ---- 只读 ----

    [HttpGet("scopes")]
    public Task<ActionResult> Scopes(int serverId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetScopesAsync(serverId, ct)));

    [HttpGet("scopes/{scopeId}/statistics")]
    public Task<ActionResult> Statistics(int serverId, string scopeId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetScopeStatisticsAsync(serverId, scopeId, ct)));

    [HttpGet("scope-statistics")]
    public Task<ActionResult> AllStatistics(int serverId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetAllScopeStatisticsAsync(serverId, ct)));

    [HttpGet("scopes/{scopeId}/leases")]
    public Task<ActionResult> Leases(int serverId, string scopeId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetLeasesAsync(serverId, scopeId, ct)));

    [HttpGet("scopes/{scopeId}/reservations")]
    public Task<ActionResult> Reservations(int serverId, string scopeId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetReservationsAsync(serverId, scopeId, ct)));

    [HttpGet("options")]
    public Task<ActionResult> Options(int serverId, [FromQuery] string? scopeId, CancellationToken ct)
        => Guard(async () => Ok(await _dhcp.GetOptionsAsync(serverId, scopeId, ct)));

    // ---- 写操作(仅 Admin)----

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("scopes")]
    public Task<ActionResult> CreateScope(int serverId, CreateScopeRequest req, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.CreateScopeAsync(serverId, req, ct);
            await _audit.LogAsync(CurrentUser, "CreateScope", $"{serverId}/{req.Name}", true);
            return Ok(new { message = "作用域已创建" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("scopes/{scopeId}")]
    public Task<ActionResult> UpdateScope(int serverId, string scopeId, UpdateScopeRequest req, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.UpdateScopeAsync(serverId, scopeId, req, ct);
            await _audit.LogAsync(CurrentUser, "UpdateScope", $"{serverId}/{scopeId}", true);
            return Ok(new { message = "作用域已更新" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("scopes/{scopeId}")]
    public Task<ActionResult> DeleteScope(int serverId, string scopeId, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.DeleteScopeAsync(serverId, scopeId, ct);
            await _audit.LogAsync(CurrentUser, "DeleteScope", $"{serverId}/{scopeId}", true);
            return Ok(new { message = "作用域已删除" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("scopes/{scopeId}/reservations")]
    public Task<ActionResult> AddReservation(int serverId, string scopeId, AddReservationRequest req, CancellationToken ct)
        => Guard(async () =>
        {
            var body = req with { ScopeId = scopeId };
            await _dhcp.AddReservationAsync(serverId, body, ct);
            await _audit.LogAsync(CurrentUser, "AddReservation", $"{serverId}/{scopeId}/{req.IPAddress}", true);
            return Ok(new { message = "保留已添加" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("scopes/{scopeId}/reservations")]
    public Task<ActionResult> RemoveReservation(int serverId, string scopeId, [FromQuery] string ip, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.RemoveReservationAsync(serverId, scopeId, ip, ct);
            await _audit.LogAsync(CurrentUser, "RemoveReservation", $"{serverId}/{scopeId}/{ip}", true);
            return Ok(new { message = "保留已删除" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("options")]
    public Task<ActionResult> SetOption(int serverId, SetOptionRequest req, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.SetOptionAsync(serverId, req, ct);
            await _audit.LogAsync(CurrentUser, "SetOption", $"{serverId}/opt{req.OptionId}", true);
            return Ok(new { message = "选项已设置" });
        });

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("options/{optionId:int}")]
    public Task<ActionResult> RemoveOption(int serverId, int optionId, [FromQuery] string? scopeId, CancellationToken ct)
        => Guard(async () =>
        {
            await _dhcp.RemoveOptionAsync(serverId, optionId, scopeId, ct);
            await _audit.LogAsync(CurrentUser, "RemoveOption", $"{serverId}/opt{optionId}", true);
            return Ok(new { message = "选项已删除" });
        });
}
