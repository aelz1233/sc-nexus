using System.IO;
using Microsoft.EntityFrameworkCore;
using SCNexus.Data;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class SettingsService
{
    private readonly DbContextOptions<NexusDbContext> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(string? databasePath = null)
    {
        databasePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "nexus.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _options = new DbContextOptionsBuilder<NexusDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options;
    }

    public async Task<PersonalSettings> LoadAsync()
    {
        await using var db = new NexusDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        return await db.PersonalSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1) ?? new();
    }

    public async Task SaveAsync(PersonalSettings snapshot)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new NexusDbContext(_options);
            var current = await db.PersonalSettings.SingleOrDefaultAsync(x => x.Id == 1);
            if (current is null) db.PersonalSettings.Add(snapshot);
            else
            {
                current.Balance = snapshot.Balance;
                current.CurrentShip = snapshot.CurrentShip;
                current.CurrentLocation = snapshot.CurrentLocation;
            }
            await db.SaveChangesAsync();
        }
        finally { _gate.Release(); }
    }
}
