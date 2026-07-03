using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public SitesController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private string CurrentUser =>
        User?.Identity?.Name ?? Request.Headers["X-Actor"].FirstOrDefault() ?? "embedded";

    [HttpGet]
    public async Task<ActionResult<List<SiteDto>>> List()
    {
        var counts = await _db.Agents
            .Where(a => a.SiteId != null)
            .GroupBy(a => a.SiteId!.Value)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SiteId, x => x.Count);
        var sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        return Ok(sites.Select(s => new SiteDto(s.Id, s.Name, counts.GetValueOrDefault(s.Id, 0))));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost]
    public async Task<ActionResult<SiteDto>> Create(CreateSiteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "工区名称不能为空" });
        var site = new Site { Name = req.Name.Trim() };
        _db.Sites.Add(site);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "CreateSite", site.Name, true);
        return Ok(new SiteDto(site.Id, site.Name, 0));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SiteDto>> Rename(int id, RenameSiteRequest req)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "工区名称不能为空" });
        site.Name = req.Name.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "RenameSite", site.Name, true);
        var count = await _db.Agents.CountAsync(a => a.SiteId == id);
        return Ok(new SiteDto(site.Id, site.Name, count));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site == null) return NotFound();
        // 移出工区下的服务器,不删除服务器本身
        var members = await _db.Agents.Where(a => a.SiteId == id).ToListAsync();
        foreach (var a in members) a.SiteId = null;
        _db.Sites.Remove(site);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "DeleteSite", site.Name, true);
        return NoContent();
    }
}
