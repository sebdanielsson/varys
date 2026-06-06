using System;
using System.IO;

namespace TranscribeApp;

/// <summary>
/// Appends timestamped log lines to %LOCALAPPDATA%\Transcribe\logs\app.log.
/// Captures the app's own messages plus the sidecar's stdout/stderr.
/// </summary>
public static class AppLog
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Transcribe", "logs");

    public static string FilePath { get; } = Path.Combine(Dir, "app.log");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
