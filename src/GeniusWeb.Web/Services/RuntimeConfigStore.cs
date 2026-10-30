using System.Text.Json;
using GeniusWeb.Contracts;
using GeniusWeb.Data;
using GeniusWeb.Data.Entities;
using GeniusWeb.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GeniusWeb.Web.Services;

/// <summary>
/// Reads and writes mutable runtime configuration (device list, password hash) to
/// the SQLite UserSettings table. Falls back to appsettings.json on first run.
/// </summary>
public sealed class RuntimeConfigStore
{
    private const string DevicesKey = "runtime.devices";
    private const string PasswordHashKey = "runtime.auth.passwordHash";

    private readonly IDbContextFactory<GeniusDbContext> _dbf;
    private readonly IConfiguration _config;
    private readonly IOptions<DeviceRegistryOptions> _seedDevices;

    public RuntimeConfigStore(
        IDbContextFactory<GeniusDbContext> dbf,
        IConfiguration config,
        IOptions<DeviceRegistryOptions> seedDevices)
    {
        _dbf = dbf;
        _config = config;
        _seedDevices = seedDevices;
    }

    public event EventHandler? DevicesChanged;

    public async Task<List<DeviceConfig>> GetDevicesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.Key == DevicesKey, ct).ConfigureAwait(false);

        if (row is not null)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<DeviceConfig>>(row.Value);
                if (list is { Count: > 0 }) return list;
            }
            catch { /* fall through to seed */ }
        }

        var seeded = _seedDevices.Value.Devices.ToList();
        if (seeded.Count == 0) return new();
        await SaveDevicesAsync(seeded, ct).ConfigureAwait(false);
        return seeded;
    }

    public async Task SaveDevicesAsync(IEnumerable<DeviceConfig> devices, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(devices);
        await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.Key == DevicesKey, ct).ConfigureAwait(false);
        if (row is null)
        {
            db.UserSettings.Add(new UserSetting
            {
                Key = DevicesKey,
                Value = json,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetPasswordHashAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.Key == PasswordHashKey, ct).ConfigureAwait(false);
        if (row is not null && !string.IsNullOrWhiteSpace(row.Value)) return row.Value;
        return _config["Auth:PasswordHash"];
    }

    public async Task SavePasswordHashAsync(string hash, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.Key == PasswordHashKey, ct).ConfigureAwait(false);
        if (row is null)
        {
            db.UserSettings.Add(new UserSetting
            {
                Key = PasswordHashKey,
                Value = hash,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Value = hash;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
