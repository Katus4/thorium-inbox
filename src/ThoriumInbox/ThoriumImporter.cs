using System.Diagnostics;
using Microsoft.Win32;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal static class ThoriumImporter
{
    public static async Task ImportAsync(string epubPath, CancellationToken cancellationToken)
    {
        string fingerprint = ThoriumLibraryReceipt.Fingerprint(epubPath);
        var desktopProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDRLab.ThoriumReader");
        string? executable = FindDesktop();
        if (executable is not null)
        {
            if (ThoriumLibraryReceipt.HasImport(desktopProfile, fingerprint))
                return;
            string safePath = StageIfNeeded(epubPath, fingerprint);
            try
            {
                var response = await RunAsync(executable, ["import", safePath], cancellationToken).ConfigureAwait(false);
                if (response.Code != 0)
                {
                    string detail = response.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
                        ?? $"Thorium exited with code {response.Code}.";
                    throw new IOException(detail);
                }
                if (!response.Output.Contains("import success:", StringComparison.Ordinal))
                    await WaitForReceiptAsync(desktopProfile, fingerprint, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (safePath != epubPath)
                    File.Delete(safePath);
            }

            return;
        }

        var storeApp = await FindStoreAppAsync(cancellationToken).ConfigureAwait(false);
        if (storeApp is null)
            throw new IOException("Thorium Reader was not found. Run Thorium Inbox setup to install it.");

        string familyName = storeApp.Split('!', 2)[0];
        string storeProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", familyName, "LocalCache", "Roaming", "EDRLab.ThoriumReader");
        if (ThoriumLibraryReceipt.HasImport(storeProfile, fingerprint))
            return;
        StoreFileActivation.Open(storeApp, epubPath);
        await WaitForReceiptAsync(storeProfile, fingerprint, cancellationToken).ConfigureAwait(false);
    }

    private static string StageIfNeeded(string epubPath, string fingerprint)
    {
        // Thorium's CLI treats brackets and braces in a path as glob expressions.
        if (epubPath.IndexOfAny(['[', ']', '{', '}', '(', ')']) < 0)
            return epubPath;
        string directory = Path.Combine(AppLocations.AppData, "import");
        if (directory.IndexOfAny(['[', ']', '{', '}', '(', ')']) >= 0)
            throw new IOException("Thorium cannot import this filename from the current Windows profile folder.");
        Directory.CreateDirectory(directory);
        string staged = Path.Combine(directory, fingerprint + ".epub");
        File.Copy(epubPath, staged, overwrite: true);
        return staged;
    }

    private static async Task WaitForReceiptAsync(string profile, string fingerprint, CancellationToken token)
    {
        // A second desktop process may exit successfully before the main reader accepts the file.
        for (int attempt = 0; attempt < 15; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (ThoriumLibraryReceipt.HasImport(profile, fingerprint))
                return;
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }

        throw new IOException("Thorium did not confirm the import. It will be tried again.");
    }

    private static string? FindDesktop()
    {
        string[] locations = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Thorium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Thorium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Thorium"),
        ];
        string? standard = locations.Select(folder => Path.Combine(folder, "Thorium.exe")).FirstOrDefault(File.Exists);
        if (standard is not null)
            return standard;

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var appPath = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Thorium.exe");
            if (appPath?.GetValue(null) is string registered && File.Exists(registered.Trim('"')))
                return registered.Trim('"');

            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
                continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is not ("Thorium" or "Thorium Reader"))
                    continue;
                if (entry.GetValue("InstallLocation") is string location
                    && File.Exists(Path.Combine(location, "Thorium.exe")))
                    return Path.Combine(location, "Thorium.exe");
            }
        }

        return null;
    }

    private static async Task<string?> FindStoreAppAsync(CancellationToken cancellationToken)
    {
        const string command = "Get-StartApps | Where-Object { $_.AppID -like 'EDRLab.ThoriumReader_*!*' } | Select-Object -First 1 -ExpandProperty AppID";
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var response = await RunAsync(powershell, ["-NoProfile", "-NonInteractive", "-Command", command], cancellationToken)
            .ConfigureAwait(false);
        string appId = response.Output.Trim();
        return response.Code == 0 && appId.StartsWith("EDRLab.ThoriumReader_", StringComparison.OrdinalIgnoreCase)
            ? appId : null;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(string executable,
        string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new IOException("Could not start the Thorium import.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Thorium took too long to respond. It will be tried again.");
        }
    }
}
