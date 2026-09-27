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
        await AddMissingColumnsAsync(db);
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
                current.CargoScu = snapshot.CargoScu;
                current.Reserve = snapshot.Reserve;
                current.AllowRisky = snapshot.AllowRisky;
            }
            await db.SaveChangesAsync();
        }
        finally { _gate.Release(); }
    }

    private static async Task AddMissingColumnsAsync(NexusDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('PersonalSettings')";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) names.Add(reader.GetString(1));
            if (!names.Contains("CargoScu")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN CargoScu INTEGER NOT NULL DEFAULT 0");
            if (!names.Contains("Reserve")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN Reserve TEXT NOT NULL DEFAULT '0'");
            if (!names.Contains("AllowRisky")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN AllowRisky INTEGER NOT NULL DEFAULT 0");
        }
        finally { await connection.CloseAsync(); }
    }
}
