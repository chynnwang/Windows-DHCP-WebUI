using DhcpWeb.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<AlertConfig> AlertConfigs => Set<AlertConfig>();
    public DbSet<LeaseLog> LeaseLogs => Set<LeaseLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Setting>().HasKey(s => s.Key);
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
        b.Entity<Agent>().HasIndex(a => a.AgentId).IsUnique();
        b.Entity<Site>().HasIndex(s => s.Name);
        b.Entity<LeaseLog>().HasIndex(l => l.SeenAtUtc);
        b.Entity<LeaseLog>().HasIndex(l => new { l.AgentId, l.ScopeId });
    }
}
