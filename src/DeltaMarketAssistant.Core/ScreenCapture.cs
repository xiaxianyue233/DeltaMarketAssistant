using System.Drawing;
using System.Drawing.Imaging;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed class ScreenCapture
{
    public Bitmap? CaptureClient(IntPtr windowHandle)
    {
        return WindowService.TryGetClientBounds(windowHandle, out var bounds)
            ? CaptureScreenRectangle(bounds)
            : null;
    }

    public Bitmap? CaptureClientRegion(IntPtr windowHandle, NormalizedRect normalizedRegion)
    {
        if (!normalizedRegion.IsValid || !WindowService.TryGetClientBounds(windowHandle, out var clientBounds))
        {
            return null;
        }

        var local = normalizedRegion.ToPixels(clientBounds.Size);
        var screen = new Rectangle(
            clientBounds.Left + local.Left,
            clientBounds.Top + local.Top,
            local.Width,
            local.Height);
        return CaptureScreenRectangle(screen);
    }

    public Bitmap? CaptureScreenRectangle(Rectangle screenRectangle)
    {
        if (screenRectangle.Width <= 0 || screenRectangle.Height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(screenRectangle.Width, screenRectangle.Height, PixelFormat.Format24bppRgb);
        using var destinationGraphics = Graphics.FromImage(bitmap);
        using var sourceGraphics = Graphics.FromHwnd(IntPtr.Zero);
        var destinationDc = destinationGraphics.GetHdc();
        var sourceDc = sourceGraphics.GetHdc();
        try
        {
            if (!NativeMethods.BitBlt(
                    destinationDc,
                    0,
                    0,
                    screenRectangle.Width,
                    screenRectangle.Height,
                    sourceDc,
                    screenRectangle.Left,
                    screenRectangle.Top,
                    NativeMethods.Srccopy))
            {
                bitmap.Dispose();
                return null;
            }
        }
        finally
        {
            destinationGraphics.ReleaseHdc(destinationDc);
            sourceGraphics.ReleaseHdc(sourceDc);
        }

        return bitmap;
    }
}
