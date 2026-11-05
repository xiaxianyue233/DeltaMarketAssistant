using System.Windows.Forms;

namespace DeltaMarketAssistant.Core;

public static class GlobalHotkeys
{
    private const uint ModNoRepeat = 0x4000;

    public static bool Register(IntPtr windowHandle, int id, Keys key) =>
        NativeMethods.RegisterHotKey(windowHandle, id, ModNoRepeat, (uint)key);

    public static bool Register(IntPtr windowHandle, int id, HotkeyChord chord) =>
        NativeMethods.RegisterHotKey(windowHandle, id, ModNoRepeat | chord.Modifiers, (uint)chord.Key);

    public static void Unregister(IntPtr windowHandle, int id) =>
        NativeMethods.UnregisterHotKey(windowHandle, id);
}
