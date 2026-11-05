using System.Drawing;
using System.Runtime.InteropServices;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed class InputService
{
    public bool ClickRegion(IntPtr windowHandle, NormalizedRect region)
    {
        return ClickRegionBurst(windowHandle, region, 1, out _);
    }

    public bool ClickRegionBurst(
        IntPtr windowHandle,
        NormalizedRect region,
        int requestedClickCount,
        out int sentClickCount)
    {
        sentClickCount = 0;
        if (!WindowService.IsForeground(windowHandle) ||
            !WindowService.TryGetClientBounds(windowHandle, out var bounds))
        {
            return false;
        }

        var clientPoint = region.CenterInPixels(bounds.Size);
        var clickCount = Math.Clamp(requestedClickCount, 1, 12);
        if (!ClickScreenPointBurst(
                new Point(bounds.Left + clientPoint.X, bounds.Top + clientPoint.Y),
                clickCount))
        {
            return false;
        }

        sentClickCount = clickCount;
        return true;
    }

    public bool PressEscape(IntPtr windowHandle) => PressKey(windowHandle, 0x1B);

    public bool PressSpace(IntPtr windowHandle) => PressKey(windowHandle, 0x20);

    private static bool PressKey(IntPtr windowHandle, ushort virtualKey)
    {
        if (!WindowService.IsForeground(windowHandle))
        {
            return false;
        }

        var inputs = new[]
        {
            new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput { VirtualKey = virtualKey }
                }
            },
            new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = NativeMethods.KeyeventfKeyup
                    }
                }
            }
        };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    private static bool ClickScreenPointBurst(Point point, int clickCount)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXvirtualscreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYvirtualscreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxvirtualscreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyvirtualscreen);
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        var absoluteX = (int)Math.Round((point.X - left) * 65535d / (width - 1));
        var absoluteY = (int)Math.Round((point.Y - top) * 65535d / (height - 1));
        var inputs = new NativeMethods.Input[1 + clickCount * 2];
        inputs[0] = MouseInput(absoluteX, absoluteY, NativeMethods.MouseeventfMove |
                                                    NativeMethods.MouseeventfAbsolute |
                                                    NativeMethods.MouseeventfVirtualdesk);
        for (var index = 0; index < clickCount; index++)
        {
            inputs[1 + index * 2] = MouseInput(
                absoluteX,
                absoluteY,
                NativeMethods.MouseeventfLeftdown |
                NativeMethods.MouseeventfAbsolute |
                NativeMethods.MouseeventfVirtualdesk);
            inputs[2 + index * 2] = MouseInput(
                absoluteX,
                absoluteY,
                NativeMethods.MouseeventfLeftup |
                NativeMethods.MouseeventfAbsolute |
                NativeMethods.MouseeventfVirtualdesk);
        }

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    private static NativeMethods.Input MouseInput(int x, int y, uint flags) => new()
    {
        Type = NativeMethods.InputMouse,
        Data = new NativeMethods.InputUnion
        {
            Mouse = new NativeMethods.MouseInput
            {
                Dx = x,
                Dy = y,
                Flags = flags
            }
        }
    };
}
