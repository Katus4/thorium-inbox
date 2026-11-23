using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace ThoriumInbox;

internal static class Program
{
    public const string InstanceMutex = @"Local\ThoriumInbox-64FA94BA";
    internal static Version Version => typeof(Program).Assembly.GetName().Version!;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(Version.ToString(3));
            return 0;
        }
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("ThoriumInbox [--background] [--version]");
            return 0;
        }

        bool inBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        string mutex = OperatingSystem.IsWindows() ? InstanceMutex :
            "ThoriumInbox-64FA94BA-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppLocations.AppData)))[..12];
        using var instance = new Mutex(initiallyOwned: true, mutex, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!inBackground)
            {
                Directory.CreateDirectory(AppLocations.AppData);
                File.WriteAllText(AppLocations.OpenWindowSignal, "");
            }
            return 0;
        }

        try
        {
            return AppBuilder.Configure<InboxApplication>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
