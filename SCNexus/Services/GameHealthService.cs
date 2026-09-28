using System.IO;
using SCNexus.Models;

namespace SCNexus.Services;

public static class GameHealthService
{
    public static IReadOnlyList<GameHealthFinding> Scan(string? gameDirectory)
    {
        if (gameDirectory is null)
            return [new("Установка игры", "Star Citizen не найден в типичных папках дисков Windows.")];
        var results = new List<GameHealthFinding>
        {
            new("Установка игры", gameDirectory)
        };
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
}
