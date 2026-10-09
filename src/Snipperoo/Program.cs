using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Snipperoo.Setup;
using Snipperoo.Ui;

namespace Snipperoo;

/// <summary>
/// Entry point. First run shows the setup wizard, installs to %LOCALAPPDATA%\Programs and restarts from there;
/// later runs go straight to the tray. Running a downloaded exe after setup updates the installed copy.
/// "--uninstall" (from Apps &amp; features) removes the app.
/// </summary>
internal static class Program
{
    private const string WelcomeArgument = "--welcome";

    [STAThread]
    private static void Main(string[] args)
    {
        // Tray and capture overlay are WinForms running inside WPF's message loop: let their exceptions reach
        // WPF's handler instead of WinForms' error dialog, and route keyboard input to WinForms windows.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();

        if (args.Contains(Installer.UninstallArgument))
        {
            RunUninstall();
            return;
        }

        var settings = AppSettings.Load();
        bool isUpdate = settings.SetupComplete && Installer.CanInstall && !Installer.IsRunningInstalled;
        if (isUpdate)
            SingleInstance.SignalExit(); // the installed copy must exit before its exe can be replaced

        var wait = isUpdate || args.Contains(WelcomeArgument) ? TimeSpan.FromSeconds(5) : TimeSpan.Zero;
        using var instance = SingleInstance.TryAcquire(wait);
        if (instance is null)
        {
            SingleInstance.SignalShowSettings();
            return;
        }

        if (isUpdate && TryInstall())
        {
            instance.Release();
            Process.Start(Installer.InstalledExe, WelcomeArgument);
            return;
        }

        var app = CreateApplication();
        if (settings.SetupComplete)
            StartTray(app, instance, settings, welcome: args.Contains(WelcomeArgument));
        else
            RunSetup(app, instance, settings);
        app.Run();
    }

    private static Application CreateApplication()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Snipperoo;component/Ui/Theme.xaml"),
        });
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        return app;
    }

    private static void RunSetup(Application app, SingleInstance instance, AppSettings settings)
    {
        var setup = new SetupWindow(settings);
        setup.Closed += (_, _) =>
        {
            if (!setup.Completed)
            {
                app.Shutdown();
                return;
            }

            settings.SetupComplete = true;
            settings.Save();

            if (Installer.CanInstall && TryInstall() && !Installer.IsRunningInstalled)
            {
                // Hand over to the installed copy so the downloaded exe can be deleted.
                Installer.StartWithWindows = setup.StartWithWindows;
                instance.Release();
                Process.Start(Installer.InstalledExe, WelcomeArgument);
                app.Shutdown();
                return;
            }

            Installer.StartWithWindows = setup.StartWithWindows;
            StartTray(app, instance, settings, welcome: true);
        };
        setup.Show();
    }

    private static bool TryInstall()
    {
        try
        {
            Installer.Install();
            return true;
        }
        catch (Exception ex)
        {
            // Not fatal: the app still works from where it was started.
            Log.Error("Install failed, running in place", ex);
            return false;
        }
    }

    private static void StartTray(Application app, SingleInstance instance, AppSettings settings, bool welcome)
    {
        var tray = new TrayApp(settings, welcome);
        void Exit()
        {
            tray.Dispose();
            app.Shutdown();
        }
        tray.ExitRequested += Exit;
        instance.Listen(
            onShowSettings: () => app.Dispatcher.BeginInvoke(tray.ShowSettings),
            onExit: () => app.Dispatcher.BeginInvoke(Exit));
    }

    private static void RunUninstall()
    {
        var app = CreateApplication();
        bool confirmed = ConfirmDialog.Show(null, "Uninstall Snipperoo?",
            "This removes the app and its settings. Your clips and screenshots stay where they are.", "Uninstall");
        if (!confirmed)
            return;

        // Close a running instance first so its files can be deleted.
        SingleInstance.SignalExit();
        using (SingleInstance.TryAcquire(TimeSpan.FromSeconds(5)))
            Installer.Uninstall();
        app.Shutdown();
    }
}
