using System.Runtime.InteropServices;

namespace ThoriumInbox;

internal static class StoreFileActivation
{
    public static void Open(string appId, string epubPath)
    {
        // The Store installs a registered app rather than a CLI on PATH.
        var shellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        var shellItemArrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
        var activationId = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(epubPath, 0, in shellItemId, out nint item));
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, in shellItemArrayId, out nint items));
            try
            {
                var activationType = Type.GetTypeFromCLSID(activationId, throwOnError: true)!;
                var manager = (IApplicationActivationManager)Activator.CreateInstance(activationType)!;
                try { manager.ActivateForFile(appId, items, "open", out _); }
                finally { Marshal.ReleaseComObject(manager); }
            }
            finally { Marshal.Release(items); }
        }
        finally { Marshal.Release(item); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(nint item, in Guid iid, out nint items);

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);

        void ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId, nint items,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
    }
}
