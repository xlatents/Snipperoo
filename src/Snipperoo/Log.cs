namespace Snipperoo;

/// <summary>Append-only text log in %APPDATA%\Snipperoo; a tray app has nowhere else to show diagnostics.</summary>
internal static class Log
{
    private static readonly Lock Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(AppSettings.DataFolder, "snipperoo.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    file.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
