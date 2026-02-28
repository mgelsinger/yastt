using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DictateTray.Core.Windows;

public sealed partial class ForegroundProcessDetector
{
    public string? GetForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
