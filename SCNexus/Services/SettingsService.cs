using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SCNexus.Data;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class SettingsService
{
    private readonly DbContextOptions<NexusDbContext> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _databaseExistedAtStartup;
    private bool _automaticBackupChecked;
    public string DatabasePath { get; }
    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");

    public SettingsService(string? databasePath = null)
    {
        databasePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "nexus.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        DatabasePath = Path.GetFullPath(databasePath);
        _databaseExistedAtStartup = File.Exists(DatabasePath) && new FileInfo(DatabasePath).Length > 0;
        _options = new DbContextOptionsBuilder<NexusDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options;
    }

    public async Task<PersonalSettings> LoadAsync()
    {
        await using var db = new NexusDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await VerifyDatabaseAsync(db);
        await CreateAutomaticBackupAsync();
        await AddMissingColumnsAsync(db);
        await EnsureFlightTablesAsync(db);
        await EnsureDataCollectionTablesAsync(db);
        return await db.PersonalSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1) ?? new();
    }

    public NexusDbContext CreateDbContext() => new(_options);

    private async Task VerifyDatabaseAsync(NexusDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            var result = await command.ExecuteScalarAsync();
            if (!string.Equals(result as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Повреждена локальная база данных. Сохранённые данные не изменены.\nФайл: {DatabasePath}\nПотребуется восстановление базы из резервной копии.");
        }
        finally { await connection.CloseAsync(); }
    }

    private static async Task EnsureFlightTablesAsync(NexusDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS PersonalShips (
                Id INTEGER NOT NULL CONSTRAINT PK_PersonalShips PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL, CargoScu INTEGER NOT NULL, Role TEXT NOT NULL, BuildNotes TEXT NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS FlightRecords (
                Id INTEGER NOT NULL CONSTRAINT PK_FlightRecords PRIMARY KEY AUTOINCREMENT,
                ShipId INTEGER NULL, ShipName TEXT NOT NULL, Origin TEXT NOT NULL,
                Destination TEXT NOT NULL, Commodity TEXT NOT NULL, StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NULL, Investment TEXT NOT NULL, Revenue TEXT NOT NULL,
                Expenses TEXT NOT NULL, Losses TEXT NOT NULL
            )
            """);
    }

    private static async Task EnsureDataCollectionTablesAsync(NexusDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS DataObservations (
                Id INTEGER NOT NULL CONSTRAINT PK_DataObservations PRIMARY KEY AUTOINCREMENT,
                Kind TEXT NOT NULL, RecordKey TEXT NOT NULL, PayloadJson TEXT NOT NULL,
                Source INTEGER NOT NULL, TimestampUtc TEXT NOT NULL, TimestampUnixMs INTEGER NOT NULL DEFAULT 0,
                Confidence REAL NOT NULL,
                Fingerprint TEXT NOT NULL
            )
            """);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('DataObservations')";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) names.Add(reader.GetString(1));
            if (!names.Contains("TimestampUnixMs"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE DataObservations ADD COLUMN TimestampUnixMs INTEGER NOT NULL DEFAULT 0");
        }
        finally { await connection.CloseAsync(); }
        await db.Database.ExecuteSqlRawAsync("UPDATE DataObservations SET TimestampUnixMs = CAST(strftime('%s', TimestampUtc) AS INTEGER) * 1000 WHERE TimestampUnixMs = 0");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_DataObservations_Fingerprint ON DataObservations (Fingerprint)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DataObservations_Kind_RecordKey_TimestampUtc ON DataObservations (Kind, RecordKey, TimestampUtc)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DataObservations_TimestampUnixMs ON DataObservations (TimestampUnixMs)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DataObservations_Kind_RecordKey_TimestampUnixMs ON DataObservations (Kind, RecordKey, TimestampUnixMs)");
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
                current.CurrentSystem = snapshot.CurrentSystem;
                current.CargoScu = snapshot.CargoScu;
                current.Reserve = snapshot.Reserve;
                current.AllowRisky = snapshot.AllowRisky;
                current.AvoidPyro = snapshot.AvoidPyro;
                current.MinimumFillPercent = snapshot.MinimumFillPercent;
                current.MinimumProfit = snapshot.MinimumProfit;
                current.GameDirectoryPath = snapshot.GameDirectoryPath;
                current.MonitorEnabled = snapshot.MonitorEnabled;
                current.MonitorIntervalSeconds = snapshot.MonitorIntervalSeconds;
                current.ShowRouteDetails = snapshot.ShowRouteDetails;
                current.OcrEnabled = snapshot.OcrEnabled;
                current.Language = snapshot.Language;
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
            if (!names.Contains("CurrentSystem")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN CurrentSystem TEXT NOT NULL DEFAULT ''");
            if (!names.Contains("AvoidPyro")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN AvoidPyro INTEGER NOT NULL DEFAULT 0");
            if (!names.Contains("MinimumFillPercent")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN MinimumFillPercent INTEGER NOT NULL DEFAULT 0");
            if (!names.Contains("MinimumProfit")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN MinimumProfit TEXT NOT NULL DEFAULT '0'");
            if (!names.Contains("GameDirectoryPath")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN GameDirectoryPath TEXT NOT NULL DEFAULT ''");
            if (!names.Contains("MonitorEnabled")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN MonitorEnabled INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("MonitorIntervalSeconds")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN MonitorIntervalSeconds INTEGER NOT NULL DEFAULT 15");
            if (!names.Contains("ShowRouteDetails")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN ShowRouteDetails INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OcrEnabled")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OcrEnabled INTEGER NOT NULL DEFAULT 0");
            if (!names.Contains("Language")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN Language TEXT NOT NULL DEFAULT 'ru'");
        }
        finally { await connection.CloseAsync(); }
    }

    public async Task BackupAsync(string destination)
    {
        if (Path.GetFullPath(destination).Equals(DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выбери другое имя для резервной копии.");
        await _gate.WaitAsync();
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
            await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
            await source.OpenAsync();
            await target.OpenAsync();
            source.BackupDatabase(target);
            await using var check = target.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            if (!string.Equals(await check.ExecuteScalarAsync() as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Созданная резервная копия не прошла проверку целостности.");
        }
        finally { _gate.Release(); }
    }

    private async Task CreateAutomaticBackupAsync()
    {
        if (_automaticBackupChecked || !_databaseExistedAtStartup) return;
        _automaticBackupChecked = true;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var name = $"{Path.GetFileNameWithoutExtension(DatabasePath)}-{DateTime.Now:yyyy-MM-dd}.db";
            var destination = Path.Combine(BackupDirectory, name);
            if (!File.Exists(destination)) await BackupAsync(destination);

            foreach (var old in Directory.EnumerateFiles(BackupDirectory, $"{Path.GetFileNameWithoutExtension(DatabasePath)}-*.db")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(7))
                File.Delete(old);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (SqliteException) { }
        catch (InvalidDataException) { }
    }
}
