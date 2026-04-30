using GeniusOnline.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeniusOnline.Data;

public sealed class GeniusDbContext : DbContext
{
    public GeniusDbContext(DbContextOptions<GeniusDbContext> options) : base(options) { }

    public DbSet<Preset> Presets => Set<Preset>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Preset>().HasIndex(p => new { p.Kind, p.DeviceId });
        b.Entity<UserSetting>().HasKey(s => s.Key);
        b.Entity<AuditLogEntry>().HasIndex(a => a.Timestamp);
    }
}
