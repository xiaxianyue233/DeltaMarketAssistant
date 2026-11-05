using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed class AutomationEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly IntPtr _windowHandle;
    private readonly bool _detectionOnly;
    private readonly string _diagnosticsDirectory;
    private readonly ScreenCapture _capture;
    private readonly InputService _input;
    private readonly AppLogger _logger;
    private readonly VisualDetector _detector;
    private readonly ResultVisualDetector _resultDetector = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private AutomationState _state = AutomationState.Paused;
    private string _stateMessage = "未启动";
    private int _consecutiveFailures;
    private long _nextPreviewAt;
    private readonly CountdownReader? _countdownReader;
    private CountdownTracker _itemCountdown = new();
    private long _nextCountdownAt;
    private string? _lastCountdownText;

    public AutomationEngine(
        AppConfig config,
        IntPtr windowHandle,
        bool detectionOnly,
        string diagnosticsDirectory,
        AppLogger logger,
        ScreenCapture? capture = null,
        InputService? input = null)
    {
        _config = config;
        _windowHandle = windowHandle;
        _detectionOnly = detectionOnly;
        _diagnosticsDirectory = diagnosticsDirectory;
        _logger = logger;
        _capture = capture ?? new ScreenCapture();
        _input = input ?? new InputService();
        _detector = new VisualDetector(config.Automation, config.Calibration ?? new CalibrationProfile());
        if (config.Automation.CountdownDriven)
            _countdownReader = new CountdownReader(config.Calibration?.CountdownTemplates);
    }

    public event Action<AutomationState, string>? StateChanged;
    public event Action<int>? FailureCountChanged;
    public event Action<Bitmap>? PreviewReady;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _worker is { IsCompleted: false };
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_worker is { IsCompleted: false })
            {
                return;
            }

            _consecutiveFailures = 0;
            FailureCountChanged?.Invoke(0);
            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cancellation.Token));
        }
    }

    public async Task StopAsync(string reason = "用户停止")
    {
        Task? worker;
        lock (_sync)
        {
            _cancellation?.Cancel();
            worker = _worker;
        }

        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected while stopping.
            }
        }

        SetState(AutomationState.Paused, reason);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_config.Calibration is not { IsComplete: true })
            {
                Pause("尚未完成校准");
                return;
            }
            if (_config.Automation.CountdownDriven && !_config.Calibration.HasCountdownRegions)
            {
                Pause("倒计时模式需要重新校准一级和二层倒计时区域；未发送输入");
                return;
            }

            if (!ValidateTarget(out var validationError))
            {
                Pause(validationError);
                return;
            }

            SetState(AutomationState.Waiting, _detectionOnly ? "仅识别：等待开放" : "等待开放购买");
            var activeGate = new StableSignalGate(_config.Automation.ConsecutiveFramesRequired);
            var emptyFrames = 0;
            var nextListCheckAt = 0L;
            var nextMainCheckAt = 0L;
            var mainScreenVerified = false;
            var initialSelectionPending = !_detectionOnly;
            var residualDialogClosed = false;
            var primaryBlankSince = 0L;
            var nextPeriodicRefreshAt = Environment.TickCount64 +
                                        TimingOptions.RefreshInterval.Clamp(_config.Automation.PeriodicRefreshIntervalMs);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!ValidateTarget(out validationError))
                {
                    Pause(validationError);
                    return;
                }

                var now = Environment.TickCount64;
                if (now >= nextMainCheckAt)
                {
                    nextMainCheckAt = now + 200;
                    using var mainAnchor = _capture.CaptureClientRegion(
                        _windowHandle,
                        _config.Calibration.MainAnchorRegion);
                    mainScreenVerified = mainAnchor is not null && _detector.IsMainScreen(mainAnchor);
                    if (!mainScreenVerified)
                    {
                        activeGate.Reset();
                        using var dialogHeader = _capture.CaptureClientRegion(
                            _windowHandle,
                            _config.Calibration.DialogHeaderRegion);
                        var dialogPresent = dialogHeader is not null && _detector.IsDialogHeaderPresent(dialogHeader);
                        if (dialogPresent)
                        {
                            if (_detectionOnly)
                            {
                                if (_config.Automation.CountdownDriven)
                                {
                                    if (!ObserveCountdown(_config.Calibration.DialogCountdownRegion))
                                    {
                                        Pause("倒计时区域捕捉失败，已暂停");
                                        return;
                                    }
                                    SetState(AutomationState.Confirming, $"仅识别：二层倒计时 {_lastCountdownText ?? "未识别"}，不发送输入");
                                }
                                else SetState(AutomationState.Idle, "当前仍是二层弹窗，请按 Esc 返回收藏主界面");
                            }
                            else if (!residualDialogClosed)
                            {
                                residualDialogClosed = true;
                                SetState(AutomationState.Recovery, "检测到二层弹窗，等待游戏自动返回，不发送 Esc");
                            }
                        }
                        else
                        {
                            SetState(AutomationState.Idle, "等待收藏主界面稳定，不会发送购买点击");
                        }
                    }
                    else
                    {
                        residualDialogClosed = false;
                        if (initialSelectionPending)
                        {
                            if (!await TrySelectFirstItemAsync(cancellationToken).ConfigureAwait(false))
                            {
                                if (!await RegisterFailureAsync("无法选择第一件收藏", cancellationToken).ConfigureAwait(false))
                                {
                                    return;
                                }
                            }

                            initialSelectionPending = false;
                        }
                    }
                }

                if (!mainScreenVerified)
                {
                    EmitPreviewIfDue();
                    await Task.Delay(_config.Automation.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (now >= nextListCheckAt)
                {
                    nextListCheckAt = now + 750;
                    using var firstItem = _capture.CaptureClientRegion(
                        _windowHandle,
                        _config.Calibration.FirstItemRegion);
                    if (firstItem is null || !VisualAnalyzer.Analyze(firstItem).HasDetail)
                    {
                        emptyFrames++;
                        if (emptyFrames >= 3)
                        {
                            SetState(AutomationState.Idle, "收藏列表为空或队首未显示");
                        }
                    }
                    else
                    {
                        if (emptyFrames >= 3 && !_detectionOnly)
                        {
                            await TrySelectFirstItemAsync(cancellationToken).ConfigureAwait(false);
                        }

                        emptyFrames = 0;
                        if (_state == AutomationState.Idle)
                        {
                            SetState(AutomationState.Waiting, "等待开放购买");
                        }
                    }
                }

                using var primary = _capture.CaptureClientRegion(
                    _windowHandle,
                    _config.Calibration.PrimaryButtonRegion);
                if (primary is null || VisualAnalyzer.Analyze(primary).IsBlank)
                {
                    activeGate.Reset();
                    primaryBlankSince = primaryBlankSince == 0 ? now : primaryBlankSince;
                    SetState(AutomationState.Idle, "一级按钮区域暂时无有效画面，等待恢复");
                    if (now - primaryBlankSince >= 5000)
                    {
                        SaveDiagnostic("primary-blank-timeout");
                        Pause("一级按钮区域持续 5 秒无有效画面，已自动暂停");
                        return;
                    }

                    await Task.Delay(_config.Automation.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                primaryBlankSince = 0;
                if (_config.Automation.CountdownDriven && !ObserveCountdown(_config.Calibration.MainCountdownRegion))
                {
                    Pause("倒计时区域捕捉失败，已暂停");
                    return;
                }

                var isActive = _detector.IsPrimaryActive(primary);
                if (!isActive)
                {
                    if (emptyFrames < 3 && _state != AutomationState.Waiting)
                    {
                        SetState(AutomationState.Waiting, _detectionOnly ? "仅识别：等待开放" : "等待开放购买");
                    }
                }

                EmitPreviewIfDue();

                if (!_detectionOnly && !isActive && emptyFrames < 3 && now >= nextPeriodicRefreshAt)
                {
                    nextPeriodicRefreshAt = now + TimingOptions.RefreshInterval.Clamp(_config.Automation.PeriodicRefreshIntervalMs);
                    activeGate.Reset();
                    if (!await TryPeriodicRefreshAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!await RegisterFailureAsync(
                                "等待开放期间定时刷新失败",
                                cancellationToken).ConfigureAwait(false))
                        {
                            return;
                        }
                    }

                    nextMainCheckAt = 0;
                    nextListCheckAt = 0;
                    mainScreenVerified = false;
                    continue;
                }

                if (activeGate.Observe(isActive))
                {
                    if (_detectionOnly)
                    {
                        SetState(AutomationState.Opening, $"仅识别：检测到一级价格按钮，倒计时 {_lastCountdownText ?? "未识别"}");
                    }
                    else
                    {
                        SetState(AutomationState.Opening, "一级价格按钮已开放，打开二层弹窗");
                        if (!_input.ClickRegion(_windowHandle, _config.Calibration.PrimaryButtonRegion))
                        {
                            if (!await RecoverAsync("一级按钮点击失败", cancellationToken).ConfigureAwait(false))
                            {
                                return;
                            }
                        }
                        else if (!await HandleDialogAsync(cancellationToken).ConfigureAwait(false))
                        {
                            return;
                        }

                        activeGate.Reset();
                        nextMainCheckAt = 0;
                        nextListCheckAt = 0;
                        mainScreenVerified = false;
                        nextPeriodicRefreshAt = Environment.TickCount64 +
                                                TimingOptions.RefreshInterval.Clamp(_config.Automation.PeriodicRefreshIntervalMs);
                    }
                }

                await Task.Delay(_config.Automation.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            _logger.Write($"未处理异常：{exception}");
            SaveDiagnostic("unhandled");
            Pause("运行异常，已自动暂停");
        }
    }

    private async Task<bool> HandleDialogAsync(CancellationToken cancellationToken)
    {
        var readyFrames = 0;
        var latePurchase = new LatePurchaseWindow();
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < _config.Automation.DialogTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out var validationError))
            {
                Pause(validationError);
                return false;
            }
            using var confirm = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration!.ConfirmButtonRegion);
            using var header = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration.DialogHeaderRegion);

            var ready = confirm is not null && header is not null && _detector.IsConfirmReady(confirm, header);
            readyFrames = ready ? readyFrames + 1 : 0;
            using var listingRow = ready
                ? _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.ListingDurationRegion)
                : null;
            if (ready && listingRow is null)
            {
                Pause("二层时间类型区域捕捉失败，未发送购买或 Esc");
                return false;
            }
            var listingVisible = listingRow is not null && _resultDetector.IsListingDurationVisible(listingRow);
            var trustedCountdownPending = HasPendingCountdown();
            var startLatePurchase = latePurchase.TryStart(listingVisible, ready, ready,
                trustedCountdownPending, false, 0, Environment.TickCount64);
            if (readyFrames >= _config.Automation.ConsecutiveFramesRequired)
            {
                if (startLatePurchase || latePurchase.IsStarted)
                    return await RunConfirmBurstAsync(
                        VisualAnalyzer.CreateFingerprint(header!), cancellationToken, latePurchase).ConfigureAwait(false);
                // Check a second listing frame before starting either purchase timing mode.
                if (listingVisible && !trustedCountdownPending)
                {
                    await Task.Delay(_config.Automation.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                _nextCountdownAt = 0;
                return await RunConfirmBurstAsync(
                    VisualAnalyzer.CreateFingerprint(header!),
                    cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(_config.Automation.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        return await RecoverAsync("一级点击后未识别到二层弹窗", cancellationToken).ConfigureAwait(false);
    }

    private bool HasPendingCountdown() => _config.Automation.CountdownDriven &&
        _itemCountdown.Decide(Environment.TickCount64,
            TimingOptions.CountdownLead.Clamp(_config.Automation.CountdownLeadMs),
            TimingOptions.CountdownTail.Clamp(_config.Automation.CountdownTailMs))
        is CountdownDecision.Waiting or CountdownDecision.Clicking;

    private async Task<bool> RunConfirmBurstAsync(
        string lockedHeaderFingerprint,
        CancellationToken cancellationToken,
        LatePurchaseWindow? latePurchase = null)
    {
        var lateWindow = latePurchase ?? new LatePurchaseWindow();
        var sessionTimer = Stopwatch.StartNew();
        var countdownDriven = _config.Automation.CountdownDriven;
        var remainingDelay = countdownDriven || lateWindow.IsStarted ? 0 : ConfirmBurstSchedule.GetRemainingDelayMs(
            sessionTimer.ElapsedMilliseconds,
            _config.Automation.ConfirmBurstStartDelayMs);
        SetState(AutomationState.Confirming, lateWindow.IsStarted
            ? "二层挂牌时间已验证，准备限时补购"
            : countdownDriven
            ? "二层已验证，按本地倒计时安排冲刺"
            : $"二层已验证，独立计时 {remainingDelay}ms 后冲刺（未读取倒计时）");
        _logger.Write($"本轮点击间隔 {TimingOptions.ClickInterval.Clamp(_config.Automation.ConfirmClickIntervalMs)}ms，" +
                      "每次一个点击，不补发积压输入");
        if (!lateWindow.IsStarted)
            _logger.Write(countdownDriven
                ? $"倒计时模式：提前 {_config.Automation.CountdownLeadMs}ms 开始，结束后续点 {_config.Automation.CountdownTailMs}ms"
                : $"本轮冲刺 {TimingOptions.BurstDuration.Clamp(_config.Automation.ConfirmBurstDurationMs)}ms，" +
                  $"额外续点 {TimingOptions.ExtraDuration.Clamp(_config.Automation.ConfirmExtraDurationMs)}ms");

        while (remainingDelay > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out var validationError))
            {
                Pause(validationError);
                return false;
            }

            var wait = Math.Min(_config.Automation.PollIntervalMs, remainingDelay);
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            remainingDelay = ConfirmBurstSchedule.GetRemainingDelayMs(
                sessionTimer.ElapsedMilliseconds,
                _config.Automation.ConfirmBurstStartDelayMs);
        }

        if (lateWindow.IsStarted) AnnounceLatePurchase();
        else SetState(AutomationState.Confirming, countdownDriven ? "等待可信倒计时，不执行固定延迟" : "独立延迟结束，开始限时冲刺");
        var burstTimer = Stopwatch.StartNew();
        var clickCount = 0;
        var clicksStopped = false;
        var clicksStoppedAt = 0L;
        var dialogSurfaceMissingSince = 0L;
        var cadence = new ConfirmClickCadence(_config.Automation.ConfirmClickIntervalMs);
        var lastResult = PurchaseResult.Unknown;
        var resultFrames = 0;
        long? lastClickAt = null;
        var minimumGap = long.MaxValue;
        var maximumGap = 0L;
        var extraPhaseAnnounced = false;
        var successFrames = 0;
        long? unavailableSince = null;
        var countdownStartAnnounced = false;
        var clickWindow = new CountdownClickWindow();
        var mainScreenGate = new StableSignalGate(
            Math.Max(1, _config.Automation.MainScreenStableFramesRequired));
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out var validationError))
            {
                Pause(validationError);
                return false;
            }

            using var confirm = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration!.ConfirmButtonRegion);
            using var mainAnchor = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration.MainAnchorRegion);

            using var refresh = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration.RefreshButtonRegion);
            using var toast = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.ToastRegion);
            using var dialogHeader = _capture.CaptureClientRegion(
                _windowHandle, _config.Calibration.DialogHeaderRegion);
            var success = CaptureSuccessSnapshot();
            if (confirm is null || mainAnchor is null || refresh is null || toast is null ||
                dialogHeader is null || !success.Captured)
            {
                SaveDiagnostic("result-capture-failed");
                Pause("购买或结果区域捕捉失败，已暂停；未发送额外点击或 Esc");
                return false;
            }
            var result = _resultDetector.Detect(toast);
            resultFrames = result != PurchaseResult.Unknown && result == lastResult ? resultFrames + 1 : 1;
            lastResult = result;
            var now = Environment.TickCount64;
            if (countdownDriven && !lateWindow.IsStarted && !clicksStopped &&
                !ObserveCountdown(_config.Calibration.DialogCountdownRegion))
            {
                SaveDiagnostic("countdown-capture-failed");
                Pause("倒计时区域捕捉失败，已停止购买");
                return false;
            }
            var countdownDecision = _itemCountdown.Decide(Environment.TickCount64,
                TimingOptions.CountdownLead.Clamp(_config.Automation.CountdownLeadMs),
                TimingOptions.CountdownTail.Clamp(_config.Automation.CountdownTailMs));
            var nowForWindow = Environment.TickCount64;
            var clickWindowActive = countdownDriven && !lateWindow.IsStarted && clickWindow.Observe(
                countdownDecision,
                nowForWindow,
                _itemCountdown.DeadlineMs,
                TimingOptions.CountdownTail.Clamp(_config.Automation.CountdownTailMs));
            var clickWindowEnded = lateWindow.IsStarted ? lateWindow.HasExpired(nowForWindow) :
                countdownDriven ? clickWindow.HasFinished(nowForWindow) :
                ConfirmBurstSchedule.HasReachedClickLimit(burstTimer.ElapsedMilliseconds,
                    _config.Automation.ConfirmBurstDurationMs, _config.Automation.ConfirmExtraDurationMs);
            var originalDialogPresent = _detector.IsOriginalDialogHeaderPresent(dialogHeader, lockedHeaderFingerprint);
            var isMainScreen = mainAnchor is not null && _detector.IsMainScreen(mainAnchor);
            var verifiedMainScreen = mainAnchor is not null && refresh is not null &&
                                     _detector.IsMainScreenWithRefresh(mainAnchor, refresh);
            var dialogSurfacePresent = originalDialogPresent &&
                                       _detector.IsLockedConfirmSurfacePresent(confirm);
            var lateDialogLost = lateWindow.IsStarted && !dialogSurfacePresent;
            successFrames = success.TitlePresent && success.SkipPresent ? successFrames + 1 : 0;
            if (!clicksStopped && (verifiedMainScreen ||
                success.TitlePresent || success.SkipPresent ||
                result != PurchaseResult.Unknown ||
                lateDialogLost ||
                clickWindowEnded))
            {
                clicksStopped = true;
                lateWindow.Stop();
                clicksStoppedAt = now;
                _logger.Write($"购买输入已停止，累计发送 {clickCount} 次点击；" +
                              (verifiedMainScreen ? "检测到一级商城刷新按钮" :
                               success.TitlePresent || success.SkipPresent ? "检测到获得外观或跳过特征" :
                               result != PurchaseResult.Unknown ? "检测到抢购结果提示" :
                               lateDialogLost ? "限时补购原二层窗口已消失" : "达到单轮连点时间上限"));
                if (lastClickAt.HasValue && clickCount > 1)
                {
                    _logger.Write($"本轮实际点击间隔范围 {minimumGap}-{maximumGap}ms");
                }
                SetState(AutomationState.Confirming, "已停止购买点击，等待结算或返回，不再续点");
            }
            if (successFrames >= 2 && !verifiedMainScreen)
            {
                return await HandleSuccessfulPurchaseAsync(lockedHeaderFingerprint, cancellationToken).ConfigureAwait(false);
            }
            if (!countdownDriven && !lateWindow.IsStarted && !clicksStopped && !extraPhaseAnnounced &&
                burstTimer.ElapsedMilliseconds >= TimingOptions.BurstDuration.Clamp(_config.Automation.ConfirmBurstDurationMs))
            {
                extraPhaseAnnounced = true;
                SetState(AutomationState.Confirming, "冲刺阶段结束，额外续点中");
            }
            if (result != PurchaseResult.Unknown && resultFrames >= 2 && !verifiedMainScreen)
            {
                return await ReturnToFavoritesAsync(
                    result == PurchaseResult.QueueFull ? "抢购队列已满" : "roll 点结果：未抢购成功",
                    lockedHeaderFingerprint,
                    cancellationToken).ConfigureAwait(false);
            }
            if (dialogSurfacePresent)
            {
                dialogSurfaceMissingSince = 0;
            }
            else
            {
                dialogSurfaceMissingSince = dialogSurfaceMissingSince == 0
                    ? now
                    : dialogSurfaceMissingSince;
            }

            var dialogSurfaceMissingMs = dialogSurfaceMissingSince == 0
                ? 0
                : now - dialogSurfaceMissingSince;
            var exitCandidate = !success.TitlePresent && !success.SkipPresent &&
                (verifiedMainScreen || DialogExitPolicy.IsExitCandidate(
                isMainScreen,
                dialogSurfacePresent,
                dialogSurfaceMissingMs));
            if (mainScreenGate.Observe(exitCandidate))
            {
                _logger.Write($"确认连续提交共发送 {clickCount} 次点击，游戏已返回一级界面");
                return await CompleteConfirmationAsync(
                    "游戏已自动返回一级界面",
                    cancellationToken).ConfigureAwait(false);
            }

            if (lateWindow.HasExpired(Environment.TickCount64))
                return await ReturnToFavoritesAsync(
                    "限时补购 2 秒窗口结束，验证后返回并刷新",
                    lockedHeaderFingerprint, cancellationToken).ConfigureAwait(false);

            if (clickCount == 0 && !lateWindow.IsStarted && !clickWindow.IsLatched &&
                !clicksStopped && !success.TitlePresent && !success.SkipPresent)
            {
                using var listingRow = _capture.CaptureClientRegion(
                    _windowHandle, ResultVisualDetector.ListingDurationRegion);
                if (listingRow is null)
                {
                    Pause("二层时间类型区域捕捉失败，未发送购买或 Esc");
                    return false;
                }
                if (lateWindow.TryStart(_resultDetector.IsListingDurationVisible(listingRow),
                    originalDialogPresent && !verifiedMainScreen,
                    _detector.IsConfirmButtonActive(confirm), HasPendingCountdown(),
                    clickWindow.IsLatched, clickCount, Environment.TickCount64))
                    AnnounceLatePurchase();
            }

            if (countdownDriven && !lateWindow.IsStarted && !clicksStopped &&
                !success.TitlePresent && !success.SkipPresent)
            {
                if (countdownDecision == CountdownDecision.Unavailable)
                {
                    unavailableSince ??= Environment.TickCount64;
                    if (!clickWindow.IsLatched &&
                        (_itemCountdown.Inconsistent || Environment.TickCount64 - unavailableSince >= 1500))
                    {
                        SaveDiagnostic("countdown-untrusted");
                        Pause(_itemCountdown.Inconsistent
                            ? "倒计时跳变或停滞，已停止购买；未退回固定延迟"
                            : "未取得可信倒计时，已停止购买；请检查倒计时校准和数字样本");
                        return false;
                    }
                }
                else unavailableSince = null;
                if (countdownDecision == CountdownDecision.Clicking && !countdownStartAnnounced)
                {
                    countdownStartAnnounced = true;
                    _logger.Write($"动态冲刺开始：距离估计开放 {_itemCountdown.DeadlineMs - Environment.TickCount64}ms，" +
                        $"结束后续点 {_config.Automation.CountdownTailMs}ms；不重新等待固定延迟");
                }
            }

            // Recheck the deadline after capture; do not queue clicks across a transition.
            if (!clicksStopped && !verifiedMainScreen && originalDialogPresent && confirm is not null &&
                _detector.IsConfirmButtonActive(confirm) && cadence.IsDue(Environment.TickCount64) &&
                (lateWindow.IsStarted ? lateWindow.CanClick(Environment.TickCount64) :
                countdownDriven ? clickWindowActive && !clickWindow.HasFinished(Environment.TickCount64) :
                !ConfirmBurstSchedule.HasReachedClickLimit(
                    burstTimer.ElapsedMilliseconds, _config.Automation.ConfirmBurstDurationMs,
                    _config.Automation.ConfirmExtraDurationMs)))
            {
                if (!_input.ClickRegionBurst(
                        _windowHandle,
                        _config.Calibration.ConfirmButtonRegion,
                        1,
                        out var sentClicks))
                {
                    Pause("连续提交时鼠标点击失败，未发送退出按键");
                    return false;
                }

                if (clickCount == 0)
                {
                    _logger.Write($"二层首次提交：验证后 {sessionTimer.ElapsedMilliseconds}ms，已发送 {sentClicks} 次点击");
                }

                clickCount += sentClicks;
                var sentAt = Environment.TickCount64;
                cadence.RecordSent(sentAt);
                if (lastClickAt.HasValue)
                {
                    var gap = sentAt - lastClickAt.Value;
                    minimumGap = Math.Min(minimumGap, gap);
                    maximumGap = Math.Max(maximumGap, gap);
                }
                lastClickAt = sentAt;
            }

            await Task.Delay(Math.Max(10, _config.Automation.PollIntervalMs), cancellationToken).ConfigureAwait(false);

            if (!lateWindow.IsStarted && clicksStopped &&
                now - clicksStoppedAt >= TimingOptions.ResultWait.Clamp(_config.Automation.ResultWaitTimeoutMs))
            {
                SaveDiagnostic("unclassified-result");
                return await ReturnToFavoritesAsync(
                    "结算等待结束，结果未分类；仅在原购买窗口仍可验证时返回",
                    lockedHeaderFingerprint,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!clicksStopped && !dialogSurfacePresent && dialogSurfaceMissingMs >= 5000)
            {
                SaveDiagnostic("dialog-surface-lost");
                Pause("二层确认区域已消失，但 5 秒内未能验证一级界面，已停止点击");
                return false;
            }
        }

        return false;
    }

    private void AnnounceLatePurchase()
    {
        SetState(AutomationState.Confirming, "挂牌时间与绿色价格已验证，限时补购 2 秒");
        _logger.Write($"限时补购：间隔 {TimingOptions.ClickInterval.Clamp(_config.Automation.ConfirmClickIntervalMs)}ms，" +
                      "最多 2000ms；不叠加冲刺/续点，不重置结束时点；结果或离开二层立即停点");
    }

    private async Task<bool> ReturnToFavoritesAsync(
        string reason,
        string lockedHeaderFingerprint,
        CancellationToken cancellationToken)
    {
        SetState(AutomationState.Recovery, $"{reason}，验证界面后单次返回");
        await Task.Delay(Math.Clamp(_config.Automation.ConfirmInputDrainMs, 100, 1000),
            cancellationToken).ConfigureAwait(false);
        var guard = new PurchaseReturnGuard();
        var timer = Stopwatch.StartNew();
        var escapeAt = 0L;
        var mainFrames = 0;
        var dialogFrames = 0;
        var successFrames = 0;
        while (timer.ElapsedMilliseconds < (guard.EscapeSent ? escapeAt + 5000 : 5000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out var error))
            {
                Pause(error);
                return false;
            }

            var success = CaptureSuccessSnapshot();
            if (!success.Captured)
            {
                SaveDiagnostic("success-return-capture-failed");
                Pause("返回时成功区域捕捉失败，未发送跳过或 Esc");
                return false;
            }
            successFrames = success.TitlePresent && success.SkipPresent ? successFrames + 1 : 0;
            if (successFrames >= 2)
                return await HandleSuccessfulPurchaseAsync(lockedHeaderFingerprint, cancellationToken).ConfigureAwait(false);
            // Never send Escape while a success animation may be replacing the purchase window.
            if (success.TitlePresent || success.SkipPresent)
            {
                mainFrames = dialogFrames = 0;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                continue;
            }
            var snapshot = CaptureReturnSnapshot(lockedHeaderFingerprint);
            mainFrames = snapshot.MainPresent ? mainFrames + 1 : 0;
            dialogFrames = snapshot.DialogPresent ? dialogFrames + 1 : 0;
            if (mainFrames >= Math.Max(3, _config.Automation.MainScreenStableFramesRequired))
            {
                return await CompleteConfirmationAsync(
                    "已返回收藏一级界面（不代表购买成功）", cancellationToken, true).ConfigureAwait(false);
            }

            if (!guard.EscapeSent && dialogFrames >= 3 && !snapshot.MainPresent)
            {
                // Revalidate immediately before the only Escape; never act on a cached result frame.
                var latest = CaptureReturnSnapshot(lockedHeaderFingerprint);
                if (guard.TryBeginEscape(latest.MainPresent, latest.DialogPresent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ValidateTarget(out error) || !_input.PressEscape(_windowHandle))
                    {
                        Pause(string.IsNullOrEmpty(error) ? "单次 Esc 发送失败，已暂停" : error);
                        return false;
                    }
                    escapeAt = timer.ElapsedMilliseconds;
                    _logger.Write("已确认仍为本轮购买窗口，发送一次 Esc；本轮不再补发 Esc");
                    mainFrames = 0;
                    dialogFrames = 0;
                }
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        SaveDiagnostic("result-return-unverified");
        Pause(guard.EscapeSent
            ? "单次 Esc 后未能确认收藏界面，已暂停；不会连续退出"
            : "结果界面未能验证为原购买窗口，已暂停；未发送 Esc");
        return false;
    }

    private (bool Captured, bool TitlePresent, bool SkipPresent) CaptureSuccessSnapshot()
    {
        using var title = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.SuccessTitleRegion);
        using var skip = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.SuccessSkipRegion);
        if (title is null || skip is null) return (false, false, false);
        return (true, _resultDetector.IsSuccessTitleVisible(title), _resultDetector.IsSuccessSkipVisible(skip));
    }

    private async Task<bool> HandleSuccessfulPurchaseAsync(
        string lockedHeaderFingerprint, CancellationToken cancellationToken)
    {
        SetState(AutomationState.Confirmed, "已识别获得枪械外观，验证后单次点击跳过");
        var guard = new SuccessReturnGuard();
        var timer = Stopwatch.StartNew();
        long skipAt = 0;
        long lastActionAt = 0;
        var continueGate = new RewardContinueGate();
        string? priorTitle = null;
        string? priorPrompt = null;
        var readyFrames = 0;
        var mainFrames = 0;
        var dialogFrames = 0;
        while (timer.ElapsedMilliseconds < (guard.SkipSent ? lastActionAt + 5000 : 5000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out var error))
            {
                Pause(error);
                return false;
            }
            var success = CaptureSuccessSnapshot();
            if (!success.Captured)
            {
                SaveDiagnostic("success-capture-failed");
                Pause("成功界面捕捉失败，未发送额外跳过");
                return false;
            }
            var snapshot = CaptureReturnSnapshot(lockedHeaderFingerprint);
            if (!snapshot.Captured)
            {
                SaveDiagnostic("success-return-capture-failed");
                Pause("成功返回区域捕捉失败，未发送空格或跳过");
                return false;
            }
            var successAbsent = !success.TitlePresent && !success.SkipPresent;
            mainFrames = snapshot.MainPresent && successAbsent ? mainFrames + 1 : 0;
            dialogFrames = snapshot.DialogPresent && successAbsent ? dialogFrames + 1 : 0;
            readyFrames = success.TitlePresent && success.SkipPresent && !snapshot.MainPresent ? readyFrames + 1 : 0;
            if (mainFrames >= Math.Max(3, _config.Automation.MainScreenStableFramesRequired))
                return await CompleteConfirmationAsync("成功展示已退出，返回收藏界面", cancellationToken).ConfigureAwait(false);
            if (guard.SkipSent && dialogFrames >= 3)
                return await ReturnToFavoritesAsync("跳过后仍在原购买窗口", lockedHeaderFingerprint, cancellationToken)
                    .ConfigureAwait(false);

            if (!guard.SkipSent && readyFrames >= 2)
            {
                var latest = CaptureSuccessSnapshot();
                var latestReturn = CaptureReturnSnapshot(lockedHeaderFingerprint);
                if (latest.Captured && latestReturn.Captured &&
                    guard.TryBeginSkip(latest.TitlePresent, latest.SkipPresent, latestReturn.MainPresent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ValidateTarget(out error) || !_input.ClickRegion(_windowHandle, ResultVisualDetector.SuccessSkipRegion))
                    {
                        Pause(string.IsNullOrEmpty(error) ? "跳过点击失败，已暂停" : error);
                        return false;
                    }
                    skipAt = timer.ElapsedMilliseconds;
                    lastActionAt = skipAt;
                    _logger.Write("成功界面已双重验证，跳过按钮已点击一次；等待返回或空格继续页");
                    readyFrames = mainFrames = dialogFrames = 0;
                }
            }

            if (guard.SkipSent && !guard.ContinueSent)
            {
                var eligible = successAbsent && !snapshot.MainPresent && !snapshot.DialogPresent;
                if (!eligible)
                {
                    continueGate.Observe(false, false, 1, 0);
                    priorTitle = priorPrompt = null;
                }
                else
                {
                    using var title = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.RewardTitleRegion);
                    using var prompt = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.ContinuePromptRegion);
                    if (title is null || prompt is null)
                    {
                        SaveDiagnostic("continue-capture-failed");
                        Pause("继续页捕捉失败，未发送空格");
                        return false;
                    }
                    var titleFingerprint = VisualAnalyzer.CreateFingerprint(title);
                    var promptFingerprint = VisualAnalyzer.CreateFingerprint(prompt);
                    var difference = priorTitle is null || priorPrompt is null ? 1 :
                        Math.Max(VisualAnalyzer.FingerprintDifference(titleFingerprint, priorTitle),
                            VisualAnalyzer.FingerprintDifference(promptFingerprint, priorPrompt));
                    var ready = continueGate.Observe(true,
                        VisualAnalyzer.Analyze(title).HasDetail && VisualAnalyzer.Analyze(prompt).HasDetail,
                        difference, timer.ElapsedMilliseconds - skipAt);
                    priorTitle = titleFingerprint;
                    priorPrompt = promptFingerprint;
                    if (ready)
                    {
                        var latestSuccess = CaptureSuccessSnapshot();
                        var latestReturn = CaptureReturnSnapshot(lockedHeaderFingerprint);
                        using var latestTitle = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.RewardTitleRegion);
                        using var latestPrompt = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.ContinuePromptRegion);
                        var stillStable = latestTitle is not null && latestPrompt is not null &&
                            VisualAnalyzer.Analyze(latestTitle).HasDetail && VisualAnalyzer.Analyze(latestPrompt).HasDetail &&
                            VisualAnalyzer.FingerprintDifference(latestTitle, titleFingerprint) <= 0.025 &&
                            VisualAnalyzer.FingerprintDifference(latestPrompt, promptFingerprint) <= 0.025;
                        if (latestSuccess.Captured && latestReturn.Captured &&
                            guard.TryBeginContinue(stillStable, latestReturn.MainPresent, latestReturn.DialogPresent,
                                latestSuccess.TitlePresent || latestSuccess.SkipPresent))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!ValidateTarget(out error) || !_input.PressSpace(_windowHandle))
                            {
                                Pause(string.IsNullOrEmpty(error) ? "空格继续发送失败，已暂停" : error);
                                return false;
                            }
                            lastActionAt = timer.ElapsedMilliseconds;
                            _logger.Write("跳过后新页面稳定，按流程发送一次空格继续；未进行文字识别，不重复发送");
                            SetState(AutomationState.Confirmed, "已发送一次空格继续，等待返回收藏");
                            readyFrames = mainFrames = dialogFrames = 0;
                        }
                    }
                }
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        SaveDiagnostic("success-skip-return-unverified");
        Pause(guard.ContinueSent ? "单次空格后未能验证返回界面，已暂停，不重复按键" :
            guard.SkipSent ? "点击跳过后未能验证返回或继续页，已暂停，不重复跳过" : "成功界面未能稳定验证，未点击跳过");
        return false;
    }

    private (bool Captured, bool MainPresent, bool DialogPresent) CaptureReturnSnapshot(string lockedHeaderFingerprint)
    {
        using var header = _capture.CaptureClientRegion(_windowHandle, _config.Calibration!.DialogHeaderRegion);
        using var confirm = _capture.CaptureClientRegion(_windowHandle, _config.Calibration.ConfirmButtonRegion);
        using var anchor = _capture.CaptureClientRegion(_windowHandle, _config.Calibration.MainAnchorRegion);
        using var refresh = _capture.CaptureClientRegion(_windowHandle, _config.Calibration.RefreshButtonRegion);
        using var back = _capture.CaptureClientRegion(_windowHandle, ResultVisualDetector.BackRegion);
        if (header is null || confirm is null || anchor is null || refresh is null || back is null)
        {
            return (false, false, false);
        }

        var headerMatches = _detector.IsOriginalDialogHeaderPresent(header, lockedHeaderFingerprint);
        var backMatches = _resultDetector.IsDialogBackVisible(back);
        var mainPresent = _detector.IsMainScreenWithRefresh(anchor, refresh) ||
            (_detector.IsMainScreen(anchor) && !headerMatches && !backMatches &&
             !_detector.IsLockedConfirmSurfacePresent(confirm));
        return (true, mainPresent, !mainPresent && headerMatches && backMatches);
    }

    private async Task<bool> CompleteConfirmationAsync(
        string message, CancellationToken cancellationToken, bool inputAlreadyDrained = false)
    {
        SetState(AutomationState.Confirmed, message);
        if (!inputAlreadyDrained)
        {
            await Task.Delay(Math.Clamp(_config.Automation.ConfirmInputDrainMs, 100, 1000),
                cancellationToken).ConfigureAwait(false);
        }

        var refreshGuard = new PurchaseReturnGuard();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await TryRefreshAndSelectAsync(true, cancellationToken, refreshGuard).ConfigureAwait(false))
            {
                return true;
            }
            if (refreshGuard.RefreshSent)
            {
                Pause("返回后刷新或重选队首失败，已暂停；本轮不会重复刷新");
                return false;
            }

            SetState(AutomationState.Recovery, "已退出二层弹窗，等待一级界面稳定");
            if (!await RegisterFailureAsync(
                    "已退出二层弹窗，但未能验证一级界面；未发送 Esc",
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return false;
    }

    private async Task<bool> RecoverAsync(string reason, CancellationToken cancellationToken)
    {
        SetState(AutomationState.Recovery, reason);
        if (!await RegisterFailureAsync(reason, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!await TryRefreshAndSelectAsync(false, cancellationToken).ConfigureAwait(false))
        {
            return await RegisterFailureAsync("恢复时未能验证主界面并刷新", cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> TryRefreshAndSelectAsync(
        bool successfulConfirmation,
        CancellationToken cancellationToken,
        PurchaseReturnGuard? refreshGuard = null)
    {
        if (successfulConfirmation)
        {
            await Task.Delay(
                TimingOptions.ReturnSettle.Clamp(_config.Automation.PostConfirmSettleMs),
                cancellationToken).ConfigureAwait(false);
        }

        var timer = Stopwatch.StartNew();
        var stableFrames = 0;
        while (timer.ElapsedMilliseconds < 1500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out _))
            {
                return false;
            }
            using var mainAnchor = _capture.CaptureClientRegion(
                _windowHandle,
                _config.Calibration!.MainAnchorRegion);
            var isMainScreen = mainAnchor is not null && _detector.IsMainScreen(mainAnchor);
            stableFrames = isMainScreen ? stableFrames + 1 : 0;
            if (stableFrames >= Math.Max(1, _config.Automation.MainScreenStableFramesRequired))
            {
                SetState(AutomationState.Refreshing, successfulConfirmation ? "确认完成，刷新收藏列表" : "异常恢复，刷新收藏列表");
                if (refreshGuard is null || refreshGuard.TryBeginRefresh())
                {
                    if (!_input.ClickRegion(_windowHandle, _config.Calibration.RefreshButtonRegion))
                    {
                        return false;
                    }
                    _logger.Write("刷新按钮已点击一次");
                }

                if (!await WaitForRefreshedQueueAsync(cancellationToken, true).ConfigureAwait(false))
                {
                    return false;
                }

                if (_state == AutomationState.Idle)
                {
                    _logger.Write("刷新完成，收藏列表为空或队首未显示");
                    return true;
                }

                if (successfulConfirmation)
                {
                    _consecutiveFailures = 0;
                    FailureCountChanged?.Invoke(0);
                }

                SetState(AutomationState.Refreshed, "刷新完成");
                SetState(AutomationState.Waiting, "队首已重新选中，等待开放购买");
                return true;
            }

            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> TrySelectFirstItemAsync(
        CancellationToken cancellationToken,
        bool writeLog = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _itemCountdown = new CountdownTracker();
        _nextCountdownAt = 0;
        _lastCountdownText = null;
        using var firstItem = _capture.CaptureClientRegion(
            _windowHandle,
            _config.Calibration!.FirstItemRegion);
        if (firstItem is null || !VisualAnalyzer.Analyze(firstItem).HasDetail)
        {
            SetState(AutomationState.Idle, "收藏列表为空或队首未显示");
            return true;
        }

        var clicked = _input.ClickRegion(_windowHandle, _config.Calibration.FirstItemRegion);
        if (clicked)
        {
            if (writeLog)
            {
                _logger.Write("第一件收藏已选中");
            }

            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        }

        return clicked;
    }

    private async Task<bool> TryPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_input.ClickRegion(_windowHandle, _config.Calibration!.RefreshButtonRegion))
        {
            return false;
        }

        if (!await WaitForRefreshedQueueAsync(cancellationToken, false).ConfigureAwait(false))
        {
            return false;
        }

        if (_state == AutomationState.Idle)
        {
            _logger.Write("等待开放期间定时刷新完成，收藏列表为空或队首未显示");
            return true;
        }

        if (_consecutiveFailures > 0)
        {
            _consecutiveFailures = 0;
            FailureCountChanged?.Invoke(0);
        }

        _logger.Write("等待开放期间已定时刷新并重新选择队首");
        return true;
    }

    private async Task<bool> WaitForRefreshedQueueAsync(CancellationToken cancellationToken, bool writeLog)
    {
        await Task.Delay(TimingOptions.RefreshWait.Clamp(_config.Automation.RefreshDelayMs),
            cancellationToken).ConfigureAwait(false);
        var timer = Stopwatch.StartNew();
        string? prior = null;
        var stableFrames = 0;
        var emptyFrames = 0;
        while (timer.ElapsedMilliseconds < 1500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateTarget(out _)) return false;
            using var anchor = _capture.CaptureClientRegion(_windowHandle, _config.Calibration!.MainAnchorRegion);
            using var first = _capture.CaptureClientRegion(_windowHandle, _config.Calibration.FirstItemRegion);
            if (anchor is null || first is null) return false;
            if (_detector.IsMainScreen(anchor))
            {
                if (VisualAnalyzer.Analyze(first).HasDetail)
                {
                    emptyFrames = 0;
                    var fingerprint = VisualAnalyzer.CreateFingerprint(first);
                    stableFrames = prior is not null &&
                        VisualAnalyzer.FingerprintDifference(fingerprint, prior) <= 0.025 ? stableFrames + 1 : 1;
                    prior = fingerprint;
                    if (stableFrames >= 2)
                        return await TrySelectFirstItemAsync(cancellationToken, writeLog).ConfigureAwait(false);
                }
                else
                {
                    stableFrames = 0;
                    prior = null;
                    if (++emptyFrames >= 3)
                    {
                        SetState(AutomationState.Idle, "刷新后收藏列表为空");
                        return true;
                    }
                }
            }
            else
            {
                stableFrames = emptyFrames = 0;
                prior = null;
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private async Task<bool> RegisterFailureAsync(string reason, CancellationToken cancellationToken)
    {
        _consecutiveFailures++;
        FailureCountChanged?.Invoke(_consecutiveFailures);
        _logger.Write($"异常 {_consecutiveFailures}/{_config.Automation.FailureLimit}：{reason}");
        SaveDiagnostic($"failure-{_consecutiveFailures}");
        if (_consecutiveFailures >= _config.Automation.FailureLimit)
        {
            Pause($"连续 {_consecutiveFailures} 次异常，已自动暂停");
            return false;
        }

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool ValidateTarget(out string error)
    {
        if (!WindowService.IsUsable(_windowHandle))
        {
            error = "目标窗口不存在、不可见或已最小化，已自动暂停";
            return false;
        }

        if (!WindowService.HasExpectedAspectRatio(_windowHandle, _config.Automation.AspectRatioTolerance))
        {
            error = "目标客户区不是 16:9，已自动暂停";
            return false;
        }

        if (!WindowService.IsForeground(_windowHandle))
        {
            error = "目标窗口失去前台，已自动暂停";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void EmitPreviewIfDue()
    {
        if (PreviewReady is null || Environment.TickCount64 < _nextPreviewAt)
        {
            return;
        }

        _nextPreviewAt = Environment.TickCount64 + 750;
        var preview = _capture.CaptureClient(_windowHandle);
        if (preview is null)
        {
            return;
        }

        using (var graphics = Graphics.FromImage(preview))
        using (var pen = new Pen(Color.LimeGreen, Math.Max(2, preview.Width / 1000f)))
        using (var font = new Font(FontFamily.GenericSansSerif, Math.Max(10, preview.Width / 180f), FontStyle.Bold))
        {
            DrawRegion(graphics, pen, font, preview.Size, _config.Calibration!.FirstItemRegion, "1");
            DrawRegion(graphics, pen, font, preview.Size, _config.Calibration.PrimaryButtonRegion, "BUY");
            DrawRegion(graphics, pen, font, preview.Size, _config.Calibration.RefreshButtonRegion, "R");
            DrawRegion(graphics, pen, font, preview.Size, _config.Calibration.ConfirmButtonRegion, "2");
            if (_config.Calibration.HasCountdownRegions)
            {
                DrawRegion(graphics, pen, font, preview.Size, _config.Calibration.MainCountdownRegion, "T1");
                DrawRegion(graphics, pen, font, preview.Size, _config.Calibration.DialogCountdownRegion, "T2");
            }
        }

        PreviewReady?.Invoke(preview);
    }

    private bool ObserveCountdown(NormalizedRect region)
    {
        var now = Environment.TickCount64;
        if (_countdownReader is null || now < _nextCountdownAt) return true;
        _nextCountdownAt = now + 100;
        using var image = _capture.CaptureClientRegion(_windowHandle, region);
        if (image is null) return false;
        var reading = _countdownReader.Read(image);
        _itemCountdown.Observe(reading?.Seconds, now);
        var text = reading?.Text;
        if (text != _lastCountdownText)
        {
            _logger.Write(text is null ? "倒计时：当前帧无法匹配，不猜测数值" :
                $"本地倒计时：{text}，模板得分 {reading!.Confidence:F2}");
            _lastCountdownText = text;
        }
        return true;
    }

    private static void DrawRegion(
        Graphics graphics,
        Pen pen,
        Font font,
        Size size,
        NormalizedRect region,
        string label)
    {
        var rectangle = region.ToPixels(size);
        graphics.DrawRectangle(pen, rectangle);
        graphics.DrawString(label, font, Brushes.LimeGreen, rectangle.Left + 3, rectangle.Top + 3);
    }

    private void SaveDiagnostic(string category)
    {
        try
        {
            Directory.CreateDirectory(_diagnosticsDirectory);
            using var frame = _capture.CaptureClient(_windowHandle);
            frame?.Save(
                Path.Combine(_diagnosticsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{category}.png"),
                ImageFormat.Png);
        }
        catch (Exception exception)
        {
            _logger.Write($"保存诊断帧失败：{exception.Message}");
        }
    }

    private void Pause(string reason)
    {
        _logger.Write(reason);
        SetState(AutomationState.Paused, reason);
    }

    private void SetState(AutomationState state, string message)
    {
        if (_state == state && _stateMessage == message)
        {
            return;
        }

        _state = state;
        _stateMessage = message;
        _logger.Write($"状态：{state} - {message}");
        StateChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }
}
