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
        await ConfigureDatabaseAsync(db);
        return await db.PersonalSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1) ?? new();
    }

    public NexusDbContext CreateDbContext() => new(_options);

    private static async Task ConfigureDatabaseAsync(NexusDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000");
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_FlightRecords_StartedAtUtc ON FlightRecords (StartedAtUtc)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_FlightRecords_EndedAtUtc ON FlightRecords (EndedAtUtc)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_PersonalShips_Name ON PersonalShips (Name)");
    }

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
                current.OverlayEnabled = snapshot.OverlayEnabled;
                current.OverlayExpanded = snapshot.OverlayExpanded;
                current.OverlayOpacity = snapshot.OverlayOpacity;
                current.OverlayTextOpacity = snapshot.OverlayTextOpacity;
                current.OverlayScale = snapshot.OverlayScale;
                current.OverlayAnchor = snapshot.OverlayAnchor;
                current.OverlayCustomLeft = snapshot.OverlayCustomLeft;
                current.OverlayCustomTop = snapshot.OverlayCustomTop;
                current.OverlayShowShip = snapshot.OverlayShowShip;
                current.OverlayShowLocation = snapshot.OverlayShowLocation;
                current.OverlayShowRoute = snapshot.OverlayShowRoute;
                current.OverlayShowMission = snapshot.OverlayShowMission;
                current.OverlayShowFreshness = snapshot.OverlayShowFreshness;
                current.OverlayHotkey = snapshot.OverlayHotkey;
                current.ActiveVoyageJson = snapshot.ActiveVoyageJson;
                current.LastSessionSummary = snapshot.LastSessionSummary;
                current.LastSessionEndedAt = snapshot.LastSessionEndedAt;
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
            if (!names.Contains("OverlayEnabled")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayEnabled INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayExpanded")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayExpanded INTEGER NOT NULL DEFAULT 0");
            if (!names.Contains("OverlayOpacity")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayOpacity REAL NOT NULL DEFAULT 0.92");
            if (!names.Contains("OverlayTextOpacity")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayTextOpacity REAL NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayScale")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayScale REAL NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayAnchor")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayAnchor TEXT NOT NULL DEFAULT 'BottomRight'");
            if (!names.Contains("OverlayCustomLeft")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayCustomLeft REAL NOT NULL DEFAULT -1");
            if (!names.Contains("OverlayCustomTop")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayCustomTop REAL NOT NULL DEFAULT -1");
            if (!names.Contains("OverlayShowShip")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayShowShip INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayShowLocation")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayShowLocation INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayShowRoute")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayShowRoute INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayShowMission")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayShowMission INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayShowFreshness")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayShowFreshness INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OverlayHotkey")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OverlayHotkey TEXT NOT NULL DEFAULT ''");
            if (!names.Contains("ActiveVoyageJson")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN ActiveVoyageJson TEXT NOT NULL DEFAULT ''");
            if (!names.Contains("LastSessionSummary")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN LastSessionSummary TEXT NOT NULL DEFAULT ''");
            if (!names.Contains("LastSessionEndedAt")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN LastSessionEndedAt TEXT NULL");
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

    public async Task<string> CreatePreUpdateBackupAsync(Version targetVersion)
    {
        Directory.CreateDirectory(BackupDirectory);
        var version = targetVersion.ToString(3);
        var destination = Path.Combine(BackupDirectory,
            $"{Path.GetFileNameWithoutExtension(DatabasePath)}-before-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        await BackupAsync(destination);
        foreach (var old in Directory.EnumerateFiles(BackupDirectory,
                     $"{Path.GetFileNameWithoutExtension(DatabasePath)}-before-v*.db")
                 .OrderByDescending(File.GetLastWriteTimeUtc).Skip(5))
        {
            try { File.Delete(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return destination;
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
