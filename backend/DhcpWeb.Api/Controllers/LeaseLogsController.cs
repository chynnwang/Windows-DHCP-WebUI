using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/lease-logs")]
[Authorize]
public class LeaseLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    public LeaseLogsController(AppDbContext db) => _db = db;

    /// <summary>租约日志:按 IP/主机名/MAC 搜索、按服务器筛选,分页返回(时间倒序)。</summary>
    [HttpGet]
    public async Task<ActionResult<LeaseLogPageDto>> List(
        [FromQuery] string? q, [FromQuery] int? serverId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.LeaseLogs.AsNoTracking().AsQueryable();
        if (serverId is int sid)
            query = query.Where(l => l.AgentId == sid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var kw = q.Trim();
            query = query.Where(l =>
                l.IpAddress.Contains(kw) ||
                (l.HostName != null && l.HostName.Contains(kw)) ||
                (l.ClientId != null && l.ClientId.Contains(kw)) ||
                l.ServerName.Contains(kw) ||
                l.ScopeId.Contains(kw));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.SeenAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LeaseLogDto(
                l.Id, l.AgentId, l.ServerName, l.ScopeId, l.ScopeName,
                l.IpAddress, l.ClientId, l.HostName, l.SeenAtUtc))
            .ToListAsync(ct);

        return Ok(new LeaseLogPageDto(total, items));
    }

    /// <summary>手动清理租约日志:保留最近 15 天。</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete]
    public async Task<ActionResult> Clear(CancellationToken ct = default)
    {
        const int retainDays = 15;
        var cutoff = DateTime.UtcNow.AddDays(-retainDays);
        var deleted = await _db.LeaseLogs.Where(l => l.SeenAtUtc < cutoff).ExecuteDeleteAsync(ct);
        return Ok(new { message = $"已清理 {deleted} 条租约日志,保留最近 {retainDays} 天", deleted, retainDays });
    }
}
