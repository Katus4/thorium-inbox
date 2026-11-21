namespace ThoriumInbox;

internal static class Program
{
    public const string InstanceMutex = @"Local\ThoriumInbox-64FA94BA";
    private const string OpenWindowEvent = @"Local\ThoriumInbox-Open-64FA94BA";

    [STAThread]
    private static int Main(string[] args)
    {
        bool inBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        using var openWindow = new EventWaitHandle(false, EventResetMode.AutoReset, OpenWindowEvent);
        using var instance = new Mutex(initiallyOwned: true, InstanceMutex, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!inBackground)
                openWindow.Set();
            return 0;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            using var application = new TrayApplication(inBackground);
            var wait = ThreadPool.RegisterWaitForSingleObject(openWindow, (_, _) => application.RequestOpen(),
                null, Timeout.Infinite, executeOnlyOnce: false);
            try
            {
                Application.Run(application);
            }
            finally
            {
                wait.Unregister(null);
            }

            return 0;
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
