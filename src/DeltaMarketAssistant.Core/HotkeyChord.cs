using System.Windows.Forms;

namespace DeltaMarketAssistant.Core;

public readonly record struct HotkeyChord(uint Modifiers, Keys Key, string Name)
{
    public static bool TryParse(string text, out HotkeyChord chord, out string error)
    {
        chord = default;
        error = "请输入 Ctrl/Alt/Shift 与字母、数字或功能键的组合";
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (var part in parts)
        {
            uint flag = part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => 2, "ALT" => 1, "SHIFT" => 4, _ => 0 };
            if (flag != 0)
            {
                if ((modifiers & flag) != 0) return false;
                modifiers |= flag;
                continue;
            }

            if (key != Keys.None) return false;
            var name = part.Length == 1 && char.IsAsciiDigit(part[0]) ? "D" + part : part;
            if (!Enum.TryParse(name, true, out key) ||
                !((key >= Keys.F1 && key <= Keys.F24) || (key >= Keys.A && key <= Keys.Z) ||
                  (key >= Keys.D0 && key <= Keys.D9) ||
                  key is Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Insert or Keys.Delete or Keys.Pause))
                return false;
        }
        if (key == Keys.None) return false;
        if (key == Keys.F12 || (key == Keys.F10 && modifiers == 0) ||
            (key == Keys.F4 && modifiers == 1) || (key == Keys.Delete && modifiers == 3))
        {
            error = "该按键由系统或校准流程保留，请使用其他组合";
            return false;
        }
        if (modifiers == 0 && !(key >= Keys.F1 && key <= Keys.F24))
        {
            error = "字母、数字和导航键需要搭配 Ctrl、Alt 或 Shift";
            return false;
        }

        var names = new List<string>();
        if ((modifiers & 2) != 0) names.Add("Ctrl");
        if ((modifiers & 1) != 0) names.Add("Alt");
        if ((modifiers & 4) != 0) names.Add("Shift");
        names.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key.ToString());
        chord = new HotkeyChord(modifiers, key, string.Join("+", names));
        error = string.Empty;
        return true;
    }

    public static string FormatKeyData(Keys keyData)
    {
        var parts = new List<string>();
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        var key = keyData & Keys.KeyCode;
        parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key.ToString());
        return string.Join("+", parts);
    }
}

public sealed class HotkeyPairRegistration(Func<int, HotkeyChord, bool> register, Action<int> unregister)
{
    private const int StartId = 101;
    private const int StopId = 102;
    private (HotkeyChord Start, HotkeyChord Stop)? _active;
    public bool IsRegistered => _active.HasValue;

    public bool TryApply(string startText, string stopText, out string error)
    {
        if (!HotkeyChord.TryParse(startText, out var start, out error) ||
            !HotkeyChord.TryParse(stopText, out var stop, out error))
            return false;
        if (start == stop)
        {
            error = "开始和停止不能使用同一组合键";
            return false;
        }
        if (_active == (start, stop)) return true;

        var prior = _active;
        Unregister();
        if (RegisterPair(start, stop))
        {
            _active = (start, stop);
            return true;
        }

        if (prior.HasValue && RegisterPair(prior.Value.Start, prior.Value.Stop))
            _active = prior;
        error = _active.HasValue
            ? "组合键已被占用，已恢复原来的开始和停止热键"
            : "组合键注册失败；请使用窗口停止按钮，并重新设置热键";
        return false;
    }

    private bool RegisterPair(HotkeyChord start, HotkeyChord stop)
    {
        var first = register(StartId, start);
        var second = first && register(StopId, stop);
        if (first && second) return true;
        if (first) unregister(StartId);
        return false;
    }

    public void Unregister()
    {
        if (!_active.HasValue) return;
        unregister(StartId);
        unregister(StopId);
        _active = null;
    }
}
