using System.Text;
using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using DhcpWeb.Api.Transport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- 配置 ----
// 内置 JWT 登录鉴权与两级角色(Admin/Viewer);SignalR hub 与 /api/ping 公开,其余接口需登录。
var useFake = builder.Configuration.GetValue("Dhcp:UseFakeTransport", false);
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? new[] { "http://localhost:5173" };

// ---- 服务注册 ----
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=dhcpweb.db"));

builder.Services.AddSingleton<AgentRegistry>();
if (useFake)
    builder.Services.AddSingleton<IDhcpTransport, FakeDhcpTransport>();
else
    builder.Services.AddSingleton<IDhcpTransport, AgentDhcpTransport>();
builder.Services.AddScoped<DhcpService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddHttpClient<FeishuAlertSender>();
builder.Services.AddHostedService<UsageAlertMonitor>();
builder.Services.AddHostedService<LeaseLogCollector>();

builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = true;
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    o.MaximumReceiveMessageSize = 1024 * 1024; // 1MB,应对大作用域租约输出
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ---- 鉴权(JWT)----
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-insecure-key-change-me-please-0123456789";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "dhcpweb";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtIssuer,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---- 初始化数据库 + 种子管理员 ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated 不会给已存在的库加新表/列,这里对工区相关 schema 做幂等补丁。
    try
    {
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"Sites\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Sites\" PRIMARY KEY AUTOINCREMENT, \"Name\" TEXT NOT NULL, \"CreatedAt\" TEXT NOT NULL)");
        var hasSiteId = db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('Agents') WHERE name = 'SiteId'")
            .AsEnumerable().FirstOrDefault();
        if (hasSiteId == 0)
            db.Database.ExecuteSqlRaw("ALTER TABLE \"Agents\" ADD COLUMN \"SiteId\" INTEGER NULL");

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"AlertConfigs\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_AlertConfigs\" PRIMARY KEY AUTOINCREMENT, \"Enabled\" INTEGER NOT NULL, \"WebhookUrl\" TEXT NOT NULL, \"Threshold\" REAL NOT NULL, \"IntervalMinutes\" INTEGER NOT NULL, \"UpdatedAt\" TEXT NOT NULL)");

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"LeaseLogs\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_LeaseLogs\" PRIMARY KEY AUTOINCREMENT, \"AgentId\" INTEGER NOT NULL, \"ServerName\" TEXT NOT NULL, \"ScopeId\" TEXT NOT NULL, \"ScopeName\" TEXT NULL, \"IpAddress\" TEXT NOT NULL, \"ClientId\" TEXT NULL, \"HostName\" TEXT NULL, \"SeenAtUtc\" TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_LeaseLogs_SeenAtUtc\" ON \"LeaseLogs\" (\"SeenAtUtc\")");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_LeaseLogs_AgentId_ScopeId\" ON \"LeaseLogs\" (\"AgentId\", \"ScopeId\")");

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"Settings\" (\"Key\" TEXT NOT NULL CONSTRAINT \"PK_Settings\" PRIMARY KEY, \"Value\" TEXT NOT NULL)");

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"Users\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Users\" PRIMARY KEY AUTOINCREMENT, \"Username\" TEXT NOT NULL, \"PasswordHash\" TEXT NOT NULL, \"Role\" TEXT NOT NULL, \"Enabled\" INTEGER NOT NULL, \"CreatedAt\" TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_Username\" ON \"Users\" (\"Username\")");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "工区 schema 补丁执行失败");
    }

    // 种子首个管理员:库中无任何用户时,建默认 admin。请登录后尽快改密码。
    try
    {
        if (!db.Users.Any())
        {
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash("admin"),
                Role = Roles.Admin,
                Enabled = true,
            });
            db.SaveChanges();
            app.Logger.LogWarning("已创建默认管理员 admin(初始密码 admin),请登录后尽快修改密码。");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "种子管理员创建失败");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<AgentHub>("/hubs/agent");
app.MapGet("/api/ping", () => Results.Ok(new { ok = true, ts = DateTime.UtcNow }));

app.Run();

public partial class Program { }
