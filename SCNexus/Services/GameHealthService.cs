using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SCNexus.Models;

namespace SCNexus.Services;

public static class GameHealthService
{
    public static IReadOnlyList<GameHealthFinding> Scan(string? gameDirectory)
    {
        var results = new List<GameHealthFinding>
        {
            new("Операционная система", $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}"),
            new("Процессор", $"Логических процессоров: {Environment.ProcessorCount}")
        };
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
            results.Add(new("Оперативная память", $"Всего {memory.TotalPhysical / 1073741824d:N1} ГБ · доступно {memory.AvailablePhysical / 1073741824d:N1} ГБ · занято {memory.MemoryLoad}%"));
        if (gameDirectory is null)
        {
            results.Add(new("Установка игры", "Не найдена. Выбери папку LIVE, PTU или EPTU в настройках."));
            return results;
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
        results.Add(new("Место на диске", $"Свободно {drive.AvailableFreeSpace / 1024d / 1024 / 1024:N1} ГБ на {drive.Name}"));
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
        return results;
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
