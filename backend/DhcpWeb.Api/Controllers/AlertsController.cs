using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FeishuAlertSender _sender;
    private readonly AuditService _audit;

    public AlertsController(AppDbContext db, FeishuAlertSender sender, AuditService audit)
    {
        _db = db;
        _sender = sender;
        _audit = audit;
    }

    private string CurrentUser =>
        User?.Identity?.Name ?? Request.Headers["X-Actor"].FirstOrDefault() ?? "embedded";

    [HttpGet("config")]
    public async Task<ActionResult<AlertConfigDto>> Get()
    {
        var cfg = await _db.AlertConfigs.FirstOrDefaultAsync();
        return Ok(cfg is null
            ? new AlertConfigDto(false, "", 95, 10)
            : new AlertConfigDto(cfg.Enabled, cfg.WebhookUrl, cfg.Threshold, cfg.IntervalMinutes));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("config")]
    public async Task<ActionResult<AlertConfigDto>> Save(SaveAlertConfigRequest req)
    {
        var threshold = Math.Clamp(req.Threshold, 1, 100);
        var interval = Math.Clamp(req.IntervalMinutes, 1, 1440);
        var webhook = (req.WebhookUrl ?? "").Trim();

        if (req.Enabled && string.IsNullOrWhiteSpace(webhook))
            return BadRequest(new { message = "启用告警时必须填写 webhook 地址" });

        var cfg = await _db.AlertConfigs.FirstOrDefaultAsync();
        if (cfg is null)
        {
            cfg = new AlertConfig();
            _db.AlertConfigs.Add(cfg);
        }
        cfg.Enabled = req.Enabled;
        cfg.WebhookUrl = webhook;
        cfg.Threshold = threshold;
        cfg.IntervalMinutes = interval;
        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "SaveAlertConfig", $"enabled={cfg.Enabled},threshold={cfg.Threshold}", true);

        return Ok(new AlertConfigDto(cfg.Enabled, cfg.WebhookUrl, cfg.Threshold, cfg.IntervalMinutes));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("test")]
    public async Task<ActionResult> Test(TestAlertRequest req, CancellationToken ct)
    {
        var webhook = (req.WebhookUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(webhook))
        {
            var cfg = await _db.AlertConfigs.FirstOrDefaultAsync();
            webhook = cfg?.WebhookUrl ?? "";
        }
        if (string.IsNullOrWhiteSpace(webhook))
            return BadRequest(new { message = "请先填写 webhook 地址" });

        try
        {
            await _sender.SendTestAsync(webhook, ct);
            await _audit.LogAsync(CurrentUser, "TestAlert", "", true);
            return Ok(new { message = "测试卡片已发送,请在飞书群查看" });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }
}
