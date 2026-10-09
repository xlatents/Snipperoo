using System.Diagnostics;
using Microsoft.Win32;

namespace Snipperoo.Setup;

/// <summary>
/// Per-user install without admin rights: the exe is copied to %LOCALAPPDATA%\Programs\Snipperoo, with a Start Menu
/// shortcut and an "Apps &amp; features" entry so Windows can uninstall it.
/// </summary>
internal static class Installer
{
    private const string AppName = "Snipperoo";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Snipperoo";
    public const string UninstallArgument = "--uninstall";

    public static string InstallFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallFolder, "Snipperoo.exe");

    private static string CurrentExe => Environment.ProcessPath!;

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Snipperoo.lnk");

    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    // A dev build runs as Snipperoo.dll next to a launcher exe; only a single-file publish can be copied on its own.
    public static bool CanInstall => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Snipperoo.dll"));

    /// <summary>The exe autostart and shortcuts should point at: the installed copy if there is one.</summary>
    private static string AppExe => File.Exists(InstalledExe) ? InstalledExe : CurrentExe;

    /// <summary>Copies the running exe into the install folder and registers it. Safe to repeat (updates in place).</summary>
    public static void Install()
    {
        if (!IsRunningInstalled)
        {
            Directory.CreateDirectory(InstallFolder);
            CopyWithRetry(CurrentExe, InstalledExe);
        }

        CreateShortcut(ShortcutPath, InstalledExe);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", InstallFolder);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArgument}");
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        Log.Info($"Installed to {InstallFolder}");
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (value)
                key.SetValue(AppName, $"\"{AppExe}\"");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Removes registrations, settings and the install folder. Captures are kept. The folder holds the running exe,
    /// so a detached shell deletes it once this process has exited.
    /// </summary>
    public static void Uninstall()
    {
        StartWithWindows = false;
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        File.Delete(ShortcutPath);
        TryDeleteFolder(AppSettings.DataFolder);

        if (Directory.Exists(InstallFolder))
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallFolder}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
    }

    // When updating, the previous version has just been told to exit and may still hold its exe for a moment.
    private static void CopyWithRetry(string source, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void CreateShortcut(string path, string target)
    {
        // WScript.Shell is the simplest way to write a .lnk without a hand-written IShellLink interop.
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "Screen clips and screenshots for Discord";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Log file may still be open; leftovers in AppData are harmless.
        }
    }
}
