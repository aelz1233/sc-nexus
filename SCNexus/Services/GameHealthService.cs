using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SCNexus.Models;

namespace SCNexus.Services;

public static class GameHealthService
{
    public static IReadOnlyList<GameHealthFinding> Scan(string? gameDirectory)
    {
        var results = new List<GameHealthFinding>
        {
            new("Операционная система", $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}"),
            new("Процессор", ReadCpuName() is { Length: > 0 } cpu
                ? $"{cpu} · логических процессоров: {Environment.ProcessorCount}"
                : $"Логических процессоров: {Environment.ProcessorCount}"),
            new("Журнал ошибок SC NEXUS", File.Exists(AppLogService.LogPath)
                ? $"Записи найдены: {AppLogService.LogPath}"
                : "Необработанные ошибки не зарегистрированы.")
        };
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
            results.Add(new("Оперативная память", $"Всего {memory.TotalPhysical / 1073741824d:N1} ГБ · доступно {memory.AvailablePhysical / 1073741824d:N1} ГБ · занято {memory.MemoryLoad}%"));
        results.Add(new("Файл подкачки", ReadPagingFileStatus()));
        if (gameDirectory is null)
        {
            results.Add(new("Установка игры", "Не найдена. Выбери папку LIVE, PTU или EPTU в настройках."));
            return Localize(results);
        }
        results.Add(new("Установка игры", gameDirectory));
        var executable = Path.Combine(gameDirectory, "Bin64", "StarCitizen.exe");
        results.Add(new("Исполняемый файл", File.Exists(executable)
            ? $"StarCitizen.exe найден · версия {FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "не указана"}"
            : "Bin64\\StarCitizen.exe не найден. Проверь выбранную папку игры."));
        var log = Path.Combine(gameDirectory, "Game.log");
        results.Add(File.Exists(log)
            ? new("Игровой журнал", $"Найден · обновлён {File.GetLastWriteTime(log):dd.MM.yyyy HH:mm}")
            : new("Игровой журнал", "Game.log пока отсутствует."));
        var drive = new DriveInfo(Path.GetPathRoot(gameDirectory)!);
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        results.Add(new("Место на диске", $"Свободно {freeGb:N1} ГБ на {drive.Name}" +
            (freeGb < 30 ? " · рекомендуется освободить место перед обновлением игры" : "")));
        var config = Path.Combine(gameDirectory, "user.cfg");
        results.Add(new("Личный конфиг", File.Exists(config) ? "user.cfg найден" : "user.cfg не найден; игра может использовать настройки по умолчанию"));
        var localisation = Path.Combine(gameDirectory, "data", "Localization");
        string?[] languages;
        try
        {
            languages = Directory.Exists(localisation)
                ? Directory.EnumerateFiles(localisation, "global.ini", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName).Select(Path.GetFileName).Where(x => x is not null).ToArray()
                : [];
        }
        catch (IOException) { languages = []; }
        catch (UnauthorizedAccessException) { languages = []; }
        results.Add(new("Файлы локализации", languages.Length == 0 ? "global.ini не найден" :
            $"Найдены папки: {string.Join(", ", languages)}"));
        AddShaderCache(results);
        AddLauncherStatus(results);
        AddHardwareFromLog(results, log);
        AddRecentLogProblems(results, log);
        return Localize(results);
    }

    private static IReadOnlyList<GameHealthFinding> Localize(IEnumerable<GameHealthFinding> results) =>
        LocalizationService.IsEnglish
            ? results.Select(x => new GameHealthFinding(LocalizationService.T(x.Title), LocalizationService.T(x.Detail))).ToArray()
            : results.ToArray();

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return Convert.ToString(key?.GetValue("ProcessorNameString"))?.Trim() ?? "";
        }
        catch (UnauthorizedAccessException) { return ""; }
    }

    private static string ReadPagingFileStatus()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            var values = key?.GetValue("PagingFiles") as string[];
            return values is { Length: > 0 }
                ? "Настроен: " + string.Join(", ", values)
                : "Не найден в настройках Windows. Отключённый файл подкачки может вызывать вылеты при нехватке RAM.";
        }
        catch (UnauthorizedAccessException) { return "Не удалось прочитать настройку Windows."; }
    }

    private static void AddShaderCache(List<GameHealthFinding> results)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Star Citizen");
        if (!Directory.Exists(root)) return;
        try
        {
            var directories = Directory.EnumerateDirectories(root).ToArray();
            var bytes = directories.SelectMany(x => SafeFiles(x)).Sum(x =>
            {
                try { return new FileInfo(x).Length; }
                catch (IOException) { return 0; }
                catch (UnauthorizedAccessException) { return 0; }
            });
            results.Add(new("Локальные кэши Star Citizen",
                $"Папок: {directories.Length} · размер около {bytes / 1024d / 1024 / 1024:N2} ГБ. Nexus ничего не удаляет автоматически."));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try { return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static void AddLauncherStatus(List<GameHealthFinding> results)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rsilauncher");
        if (!Directory.Exists(root)) return;
        try
        {
            var latest = Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories)
                .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            results.Add(new("RSI Launcher", latest is null ? "Папка лаунчера найдена; журналы отсутствуют."
                : $"Последний журнал: {latest.Name} · {latest.LastWriteTime:dd.MM.yyyy HH:mm}"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AddRecentLogProblems(List<GameHealthFinding> results, string log)
    {
        if (!File.Exists(log)) return;
        string text;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 2 * 1024 * 1024) stream.Seek(-2 * 1024 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (stream.Position > 0) reader.ReadLine();
            text = reader.ReadToEnd();
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        var known = new (string Token, string Explanation)[]
        {
            ("EXCEPTION_ACCESS_VIOLATION", "Обнаружен C0000005 / access violation. Причину нужно уточнять по контексту журнала и драйверам."),
            ("DXGI_ERROR_DEVICE_REMOVED", "Зафиксирован сброс графического драйвера или устройства GPU."),
            ("GPU crash", "В журнале есть признак сбоя GPU."),
            ("Is out of system memory", "Игра сообщила о нехватке системной памяти."),
            ("watchdog", "В журнале встречается watchdog: возможное зависание игрового потока."),
            ("ERROR 30000", "Обнаружена сетевая ошибка 30000."),
            ("ERROR 30007", "Обнаружена ошибка соединения 30007."),
            ("ERROR 19000", "Обнаружена ошибка входа 19000.")
        };
        var found = known.Where(x => text.Contains(x.Token, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Explanation).Distinct().ToArray();
        results.Add(new("Последние ошибки игры", found.Length == 0
            ? "В последних 2 МБ Game.log известные критические сигнатуры не найдены."
            : string.Join(" ", found)));
    }

    private static void AddHardwareFromLog(List<GameHealthFinding> results, string log)
    {
        if (!File.Exists(log)) return;
        string text;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var buffer = new char[512 * 1024];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            text = new string(buffer, 0, read);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        var gpu = Regex.Match(text, @"D3D Adapter:\s*Description:\s*(?<v>[^\r\n]+)", RegexOptions.IgnoreCase).Groups["v"].Value.Trim();
        var driver = Regex.Match(text, @"D3D Adapter:\s*Driver version \(UMD\):\s*(?<v>[^\r\n]+)", RegexOptions.IgnoreCase).Groups["v"].Value.Trim();
        var videoMemory = Regex.Match(text, @"D3D Adapter:\s*DedicatedVidMem\s*=\s*(?<v>\d+)", RegexOptions.IgnoreCase).Groups["v"].Value;
        if (!string.IsNullOrWhiteSpace(gpu))
            results.Add(new("Графический адаптер", gpu +
                (string.IsNullOrWhiteSpace(videoMemory) ? "" : $" · VRAM {videoMemory} МБ") +
                (string.IsNullOrWhiteSpace(driver) ? "" : $" · драйвер {driver}")));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
