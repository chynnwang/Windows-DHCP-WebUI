using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DhcpWeb.Api.Controllers;

/// <summary>获取 Agent 下载地址与预填好的安装命令。密钥经接口动态下发,不进前端静态资源。</summary>
[ApiController]
[Route("api/agent")]
[Authorize(Roles = Roles.Admin)]
public class AgentSetupController : ControllerBase
{
    // 平台对外地址的可选覆盖值(存 Settings 表);未设置时回退为当前访问地址。
    private const string PlatformUrlKey = AgentSetup.PlatformUrlKey;

    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public AgentSetupController(IConfiguration config, AppDbContext db)
    {
        _config = config;
        _db = db;
    }

    [HttpGet("setup-info")]
    public async Task<ActionResult> SetupInfo()
    {
        // 自动识别:经 Nginx 反代时 Host 为客户端访问地址(如 dhcp.example.com:8090)
        var autoUrl = $"{Request.Scheme}://{Request.Host}";
        var configured = (await _db.Settings.FindAsync(PlatformUrlKey))?.Value;
        var platformUrl = string.IsNullOrWhiteSpace(configured) ? autoUrl : configured!;

        var downloadUrl = $"{platformUrl}/download/DhcpAgent.exe";
        // 密钥优先取 Settings 表覆盖值,未设置时回退 appsettings 默认值。
        var secretOverride = (await _db.Settings.FindAsync(AgentSetup.EnrollmentSecretKey))?.Value;
        var secret = string.IsNullOrWhiteSpace(secretOverride) ? (_config["Agent:EnrollmentSecret"] ?? "") : secretOverride!;
        var installCommand = $".\\DhcpAgent.exe install --server {platformUrl} --secret {secret}";

        return Ok(new
        {
            downloadUrl,
            platformUrl,
            autoPlatformUrl = autoUrl,
            platformUrlConfigured = !string.IsNullOrWhiteSpace(configured),
            enrollmentSecret = secret,
            enrollmentSecretConfigured = !string.IsNullOrWhiteSpace(secretOverride),
            installCommand,
        });
    }

    /// <summary>设置或清除 Agent 连接密钥。secret 留空表示恢复为服务器默认密钥。
    /// 注意:修改后,已安装的旧 Agent 会因密钥不符而无法重连,需用新密钥重新安装。</summary>
    [HttpPut("enrollment-secret")]
    public async Task<ActionResult> SetEnrollmentSecret(SetEnrollmentSecretRequest req)
    {
        var setting = await _db.Settings.FindAsync(AgentSetup.EnrollmentSecretKey);
        var value = (req.Secret ?? "").Trim();

        if (string.IsNullOrEmpty(value))
        {
            if (setting != null)
            {
                _db.Settings.Remove(setting);
                await _db.SaveChangesAsync();
            }
            return Ok(new { message = "已恢复为服务器默认密钥" });
        }

        if (value.Length < 4 || value.Contains(' '))
            return BadRequest(new { message = "密钥至少 4 位且不能包含空格" });

        if (setting == null)
            _db.Settings.Add(new Setting { Key = AgentSetup.EnrollmentSecretKey, Value = value });
        else
            setting.Value = value;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Agent 连接密钥已更新,新接入的 Agent 请使用新密钥;已安装的旧 Agent 需用新密钥重装。" });
    }

    /// <summary>设置或清除「接入服务器」使用的平台对外地址。url 留空表示恢复为自动识别当前访问地址。</summary>
    [HttpPut("platform-url")]
    public async Task<ActionResult> SetPlatformUrl(SetCallbackUrlRequest req)
    {
        var setting = await _db.Settings.FindAsync(PlatformUrlKey);

        if (string.IsNullOrWhiteSpace(req.Url))
        {
            if (setting != null)
            {
                _db.Settings.Remove(setting);
                await _db.SaveChangesAsync();
            }
            return Ok(new { message = "已恢复为自动识别访问地址" });
        }

        var url = NormalizeUrl(req.Url);
        if (url == null)
            return BadRequest(new { message = "地址格式无效,请输入形如 http://主机名或IP:端口 的地址" });

        if (setting == null)
            _db.Settings.Add(new Setting { Key = PlatformUrlKey, Value = url });
        else
            setting.Value = url;
        await _db.SaveChangesAsync();
        return Ok(new { message = $"平台地址已设为 {url}", url });
    }

    // 允许只填主机名/IP(:端口),自动补 http://;仅接受 http/https 绝对地址。
    private static string? NormalizeUrl(string? input)
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
}
