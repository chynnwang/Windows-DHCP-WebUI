namespace DhcpWeb.Api.Models.Dtos;

// ---- 服务器(Agent)----
public record ServerDto(
    int Id, string AgentId, string Name, string? Hostname, string? DhcpVersion,
    string? AgentVersion, bool Online, DateTime? LastSeenUtc,
    int? SiteId, string? SiteName);
public record RenameServerRequest(string Name);
public record SetCallbackUrlRequest(string Url);
public record SetEnrollmentSecretRequest(string? Secret);

// ---- 告警 ----
public record AlertConfigDto(bool Enabled, string WebhookUrl, double Threshold, int IntervalMinutes);
public record SaveAlertConfigRequest(bool Enabled, string WebhookUrl, double Threshold, int IntervalMinutes);
public record TestAlertRequest(string? WebhookUrl);

// ---- 租约日志 ----
public record LeaseLogDto(
    int Id, int AgentId, string ServerName, string ScopeId, string? ScopeName,
    string IpAddress, string? ClientId, string? HostName, DateTime SeenAtUtc);
public record LeaseLogPageDto(int Total, List<LeaseLogDto> Items);

// ---- 工区 ----
public record SiteDto(int Id, string Name, int ServerCount);
public record CreateSiteRequest(string Name);
public record RenameSiteRequest(string Name);
public record AssignSiteRequest(int? SiteId);

// ---- 作用域 ----
public record CreateScopeRequest(
    string Name, string StartRange, string EndRange, string SubnetMask,
    string? Description, int? LeaseDays, bool Active,
    string? Gateway = null, string[]? DnsServers = null, string? DnsDomain = null);

public record UpdateScopeRequest(
    string? Name, string? Description, int? LeaseDays, bool? Active);

// ---- 保留(静态绑定)----
public record AddReservationRequest(
    string ScopeId, string IPAddress, string ClientId, string? Name, string? Description);

// ---- 选项 ----
public record SetOptionRequest(string? ScopeId, int OptionId, string[] Values);

// ---- 用户 / 鉴权 ----
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, DateTime ExpiresAt);
public record UserDto(int Id, string Username, string Role, bool Enabled, DateTime CreatedAt);
public record MeDto(string Username, string Role);
public record CreateUserRequest(string Username, string Password, string Role);
public record UpdateUserRequest(string? Role, bool? Enabled, string? Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
