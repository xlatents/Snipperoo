namespace Snipperoo;

/// <summary>
/// One running Snipperoo per user session. Another launch signals the running one to show Settings;
/// the uninstaller signals it to exit.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Snipperoo.SingleInstance";
    private const string ShowEventName = @"Local\Snipperoo.ShowSettings";
    private const string ExitEventName = @"Local\Snipperoo.Exit";

    private readonly Mutex _mutex;
    private readonly List<RegisteredWaitHandle> _waits = [];
    private bool _owned = true;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the instance lock, or null if Snipperoo is already running.</summary>
    public static SingleInstance? TryAcquire(TimeSpan wait = default)
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            if (mutex.WaitOne(wait))
                return new SingleInstance(mutex);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; the mutex is ours now.
            return new SingleInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public static void SignalShowSettings() => Signal(ShowEventName);

    public static void SignalExit() => Signal(ExitEventName);

    /// <summary>Runs the callbacks (on the thread pool) when another process signals.</summary>
    public void Listen(Action onShowSettings, Action onExit)
    {
        _waits.Add(Register(ShowEventName, onShowSettings));
        _waits.Add(Register(ExitEventName, onExit));
    }

    /// <summary>Lets a new process take over before this one has exited (used after installing).</summary>
    public void Release()
    {
        if (!_owned)
            return;
        _owned = false;
        _mutex.ReleaseMutex();
    }

    public void Dispose()
    {
        foreach (var wait in _waits)
            wait.Unregister(null);
        Release();
        _mutex.Dispose();
    }

    private static RegisteredWaitHandle Register(string name, Action callback)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        return ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static void Signal(string name)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }
}
