using System.Globalization;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed record TimingOption(
    string Id, string Label, int Minimum, int Maximum, int Increment,
    Func<AutomationSettings, int> Read, Action<AutomationSettings, int> Write)
{
    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);
}

public static class TimingOptions
{
    public static readonly TimingOption CountdownLead = new(nameof(AutomationSettings.CountdownLeadMs),
        "提前开始(ms)", 0, 5000, 100, s => s.CountdownLeadMs, (s, v) => s.CountdownLeadMs = v);
    public static readonly TimingOption CountdownTail = new(nameof(AutomationSettings.CountdownTailMs),
        "结束后续点(ms)", 0, 10000, 100, s => s.CountdownTailMs, (s, v) => s.CountdownTailMs = v);
    public static readonly TimingOption RefreshInterval = new(nameof(AutomationSettings.PeriodicRefreshIntervalMs),
        "刷新间隔(ms)", 500, 10000, 250, s => s.PeriodicRefreshIntervalMs, (s, v) => s.PeriodicRefreshIntervalMs = v);
    public static readonly TimingOption StartDelay = new(nameof(AutomationSettings.ConfirmBurstStartDelayMs),
        "冲刺延迟(ms)", 0, 6000, 100, s => s.ConfirmBurstStartDelayMs, (s, v) => s.ConfirmBurstStartDelayMs = v);
    public static readonly TimingOption ClickInterval = new(nameof(AutomationSettings.ConfirmClickIntervalMs),
        "点击间隔(ms)", 100, 1000, 25, s => s.ConfirmClickIntervalMs, (s, v) => s.ConfirmClickIntervalMs = v);
    public static readonly TimingOption BurstDuration = new(nameof(AutomationSettings.ConfirmBurstDurationMs),
        "冲刺时长(ms)", 500, int.MaxValue, 100, s => s.ConfirmBurstDurationMs, (s, v) => s.ConfirmBurstDurationMs = v);
    public static readonly TimingOption ExtraDuration = new(nameof(AutomationSettings.ConfirmExtraDurationMs),
        "额外续点(ms)", 0, 2000, 100, s => s.ConfirmExtraDurationMs, (s, v) => s.ConfirmExtraDurationMs = v);
    public static readonly TimingOption RefreshWait = new(nameof(AutomationSettings.RefreshDelayMs),
        "刷新等待(ms)", 50, 1000, 25, s => s.RefreshDelayMs, (s, v) => s.RefreshDelayMs = v);
    public static readonly TimingOption ReturnSettle = new(nameof(AutomationSettings.PostConfirmSettleMs),
        "返回稳定(ms)", 0, 1000, 25, s => s.PostConfirmSettleMs, (s, v) => s.PostConfirmSettleMs = v);
    public static readonly TimingOption ResultWait = new(nameof(AutomationSettings.ResultWaitTimeoutMs),
        "结果等待(ms)", 2000, 60000, 500, s => s.ResultWaitTimeoutMs, (s, v) => s.ResultWaitTimeoutMs = v);

    public static IReadOnlyList<TimingOption> All { get; } =
        [RefreshInterval, CountdownLead, CountdownTail, StartDelay, ClickInterval, BurstDuration, ExtraDuration, RefreshWait, ReturnSettle, ResultWait];

    public static bool TryApply(AutomationSettings settings, IReadOnlyDictionary<string, string> values, out string error)
    {
        var parsed = new Dictionary<TimingOption, int>();
        foreach (var option in All)
        {
            if (!values.TryGetValue(option.Id, out var text) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ||
                value < option.Minimum || value > option.Maximum)
            {
                error = $"{option.Label} 必须为 {option.Minimum}-{option.Maximum} 之间的整数";
                return false;
            }
            parsed[option] = value;
        }
        foreach (var (option, value) in parsed) option.Write(settings, value);
        error = string.Empty;
        return true;
    }
}
