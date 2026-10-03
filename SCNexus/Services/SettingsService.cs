using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SCNexus.Data;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class SettingsService
{
    private const string CorruptDatabasePrefix = "Повреждена локальная база данных.";
    private readonly DbContextOptions<NexusDbContext> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _databaseExistedAtStartup;
    private bool _automaticBackupChecked;
    public string DatabasePath { get; }
    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");
    public string? StartupRecoveryMessage { get; private set; }
    public bool NeedsLanguageSelection { get; private set; }

    public SettingsService(string? databasePath = null)
    {
        databasePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "nexus.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        DatabasePath = Path.GetFullPath(databasePath);
        _databaseExistedAtStartup = File.Exists(DatabasePath) && new FileInfo(DatabasePath).Length > 0;
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            DefaultTimeout = 10,
            Pooling = true
        }.ToString();
        _options = new DbContextOptionsBuilder<NexusDbContext>()
            .UseSqlite(connection).Options;
    }

    public async Task<PersonalSettings> LoadAsync()
    {
        try { return await LoadCoreAsync(); }
        catch (Exception ex) when (_databaseExistedAtStartup && IsDatabaseCorruption(ex))
        {
            await RestoreLatestVerifiedBackupAsync(ex);
            return await LoadCoreAsync();
        }
    }

    private async Task<PersonalSettings> LoadCoreAsync()
    {
        await using var db = new NexusDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await VerifyDatabaseAsync(db);
        await CreateAutomaticBackupAsync();
        await AddMissingColumnsAsync(db);
        await EnsureFlightTablesAsync(db);
        await EnsureDataCollectionTablesAsync(db);
        await ConfigureDatabaseAsync(db);
        var saved = await db.PersonalSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        NeedsLanguageSelection = saved is null;
        var settings = saved ?? new();
        NormalizeSettings(settings);
        return settings;
    }

    public NexusDbContext CreateDbContext() => new(_options);

    internal async Task<T> InTransactionAsync<T>(Func<NexusDbContext, Task<T>> action)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await action(db);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        }
        finally { _gate.Release(); }
    }

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
                throw new InvalidDataException($"{CorruptDatabasePrefix} Сохранённые данные не изменены.\nФайл: {DatabasePath}\nПотребуется восстановление базы из резервной копии.");
        }
        finally { await connection.CloseAsync(); }
    }

    private async Task RestoreLatestVerifiedBackupAsync(Exception original)
    {
        await _gate.WaitAsync();
        try
        {
            var backup = await FindLatestVerifiedBackupAsync();
            if (backup is null) throw original;

            SqliteConnection.ClearAllPools();
            var staged = DatabasePath + ".restore-" + Guid.NewGuid().ToString("N");
            var preserved = Path.Combine(Path.GetDirectoryName(DatabasePath)!,
                $"{Path.GetFileNameWithoutExtension(DatabasePath)}-corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            var moved = new List<(string Original, string Preserved)>();
            try
            {
                // SQLite backup includes any committed WAL data. Prepare it before touching the original.
                await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = staged, Pooling = false }.ToString()))
                {
                    await source.OpenAsync();
                    await target.OpenAsync();
                    source.BackupDatabase(target);
                }
                if (!await IsHealthyDatabaseAsync(staged)) throw new InvalidDataException("Резервная копия не содержит данных Nexus.");
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    if (!File.Exists(DatabasePath + suffix)) continue;
                    File.Move(DatabasePath + suffix, preserved + suffix);
                    moved.Add((DatabasePath + suffix, preserved + suffix));
                }
                File.Move(staged, DatabasePath);
            }
            catch
            {
                foreach (var item in moved.AsEnumerable().Reverse())
                    File.Move(item.Preserved, item.Original);
                throw;
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            StartupRecoveryMessage = $"Повреждённая база восстановлена из резервной копии {Path.GetFileName(backup)}.";
            AppLogService.Write($"Database recovery: {DatabasePath}; backup: {backup}", original);
        }
        catch (Exception recoveryError) when (!ReferenceEquals(recoveryError, original))
        {
            AppLogService.Write("Database recovery", recoveryError);
            throw original;
        }
        finally { _gate.Release(); }
    }

    private async Task<string?> FindLatestVerifiedBackupAsync()
    {
        if (!Directory.Exists(BackupDirectory)) return null;
        foreach (var candidate in Directory.EnumerateFiles(BackupDirectory,
                     $"{Path.GetFileNameWithoutExtension(DatabasePath)}-*.db")
                 .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (await IsHealthyDatabaseAsync(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<bool> IsHealthyDatabaseAsync(string path)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            if (!string.Equals(await command.ExecuteScalarAsync() as string, "ok", StringComparison.OrdinalIgnoreCase)) return false;
            command.CommandText = "SELECT Balance, CurrentShip, CurrentLocation FROM PersonalSettings WHERE Id = 1";
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() && !reader.IsDBNull(0) &&
                decimal.TryParse(reader.GetString(0), System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out _) &&
                !reader.IsDBNull(1) && !reader.IsDBNull(2);
        }
        catch (SqliteException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsDatabaseCorruption(Exception exception)
    {
        if (exception is InvalidDataException { Message: var message } && message.StartsWith(CorruptDatabasePrefix, StringComparison.Ordinal)) return true;
        if (exception is SqliteException { SqliteErrorCode: 11 or 26 }) return true;
        return exception.InnerException is not null && IsDatabaseCorruption(exception.InnerException);
    }

    public static string DescribeSaveFailure(Exception exception)
    {
        var sqlite = UnwrapSqliteException(exception);
        return sqlite?.SqliteErrorCode switch
        {
            5 or 6 => "Не удалось сохранить данные: база занята. Повтори действие через несколько секунд.",
            11 or 26 => "Не удалось сохранить данные: база повреждена. Перезапусти SC NEXUS для восстановления из резервной копии.",
            _ when sqlite is not null => "Не удалось сохранить данные SQLite. Подробности записаны в локальный журнал приложения.",
            _ => "Не удалось сохранить данные. Подробности записаны в локальный журнал приложения."
        };
    }

    private static SqliteException? UnwrapSqliteException(Exception exception) => exception switch
    {
        SqliteException sqlite => sqlite,
        _ when exception.InnerException is not null => UnwrapSqliteException(exception.InnerException),
        _ => null
    };

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
            NormalizeSettings(snapshot);
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
                current.ContractEarningsEnabled = snapshot.ContractEarningsEnabled;
                current.ContractEarningsJson = snapshot.ContractEarningsJson ?? "[]";
                current.OcrEnabled = snapshot.OcrEnabled;
                current.AutoFleetOcrEnabled = snapshot.AutoFleetOcrEnabled;
                current.FleetOcrAutoScroll = snapshot.FleetOcrAutoScroll;
                current.OcrIntervalSeconds = Math.Clamp(snapshot.OcrIntervalSeconds, 5, 30);
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
                current.ActiveShoppingJson = snapshot.ActiveShoppingJson;
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
            if (!names.Contains("AutoFleetOcrEnabled")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN AutoFleetOcrEnabled INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("FleetOcrAutoScroll")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN FleetOcrAutoScroll INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("OcrIntervalSeconds")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN OcrIntervalSeconds INTEGER NOT NULL DEFAULT 5");
            if (!names.Contains("ContractEarningsEnabled")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN ContractEarningsEnabled INTEGER NOT NULL DEFAULT 1");
            if (!names.Contains("ContractEarningsJson")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN ContractEarningsJson TEXT NOT NULL DEFAULT '[]'");
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
            if (!names.Contains("ActiveShoppingJson")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalSettings ADD COLUMN ActiveShoppingJson TEXT NOT NULL DEFAULT ''");
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
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
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

    private static void NormalizeSettings(PersonalSettings settings)
    {
        settings.CurrentShip ??= "Не выбран";
        settings.CurrentLocation ??= "Не указана";
        settings.CurrentSystem ??= "";
        settings.GameDirectoryPath ??= "";
        settings.Language ??= "ru";
        settings.OverlayAnchor ??= "BottomRight";
        settings.OverlayHotkey ??= "";
        settings.ActiveVoyageJson ??= "";
        settings.ActiveShoppingJson ??= "";
        settings.LastSessionSummary ??= "";
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
                         .Where(path => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path)[(Path.GetFileNameWithoutExtension(DatabasePath).Length + 1)..],
                             "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(7))
                File.Delete(old);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (SqliteException) { }
        catch (InvalidDataException) { }
    }
}
