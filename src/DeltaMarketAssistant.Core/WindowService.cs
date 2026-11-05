using System.Diagnostics;
using System.Drawing;
using System.Text;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public static class WindowService
{
    public static IReadOnlyList<WindowInfo> EnumerateVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            if (!TryGetClientBounds(handle, out var bounds) || bounds.Width < 640 || bounds.Height < 360)
            {
                return true;
            }

            var text = new StringBuilder(NativeMethods.GetWindowTextLength(handle) + 1);
            NativeMethods.GetWindowText(handle, text, text.Capacity);
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);

            var processName = "unknown";
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                // The process may exit while the window list is being built.
            }

            windows.Add(new WindowInfo(handle, text.ToString(), processName, bounds.Width, bounds.Height));
            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool TryGetClientBounds(IntPtr handle, out Rectangle screenBounds)
    {
        screenBounds = Rectangle.Empty;
        if (!NativeMethods.IsWindow(handle) || !NativeMethods.GetClientRect(handle, out var client))
        {
            return false;
        }

        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            return false;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        screenBounds = new Rectangle(origin.X, origin.Y, width, height);
        return true;
    }

    public static bool IsForeground(IntPtr handle) => NativeMethods.GetForegroundWindow() == handle;

    public static bool IsUsable(IntPtr handle) =>
        NativeMethods.IsWindow(handle) && NativeMethods.IsWindowVisible(handle) && !NativeMethods.IsIconic(handle);

    public static bool HasExpectedAspectRatio(IntPtr handle, double tolerance)
    {
        if (!TryGetClientBounds(handle, out var bounds))
        {
            return false;
        }

        var ratio = bounds.Width / (double)bounds.Height;
        return Math.Abs(ratio - 16d / 9d) <= tolerance;
    }

    public static bool BringToForeground(IntPtr handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        }

        return NativeMethods.SetForegroundWindow(handle);
    }
}
