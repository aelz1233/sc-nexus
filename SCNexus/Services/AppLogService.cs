using System.IO;
using System.Text;

namespace SCNexus.Services;

public static class AppLogService
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "logs");
    public static string LogPath => Path.Combine(DirectoryPath, "sc-nexus.log");

    public static void Write(string area, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
                    File.Move(LogPath, Path.Combine(DirectoryPath, "sc-nexus.previous.log"), true);
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] {area}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
