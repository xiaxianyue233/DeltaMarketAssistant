using System.Drawing;
using System.Drawing.Imaging;
using DeltaMarketAssistant.Core;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Tests;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Run("归一化坐标适配三种分辨率", TestNormalizedCoordinates);
            Run("连续两帧只触发一次", TestStableSignalGate);
            Run("每轮从二层验证时重新计时", TestConfirmBurstSchedule);
            Run("冲刺尊重自定义时长且计时不溢出", TestConfirmClickLimit);
            Run("点击节奏不补发积压输入", TestClickCadence);
            Run("返回保护禁止多次 Esc", TestReturnGuard);
            Run("限时补购只接受连续验证的新二层绿色按钮", TestLatePurchaseGate);
            Run("限时补购严格两秒且保留点击间隔", TestLatePurchaseCadence);
            Run("限时补购停止后禁止重启和延长", TestLatePurchaseStop);
            Run("成功界面只允许一次经过验证的跳过", TestSuccessReturnGuard);
            Run("跳过后空格继续需要稳定页面且每轮仅一次", TestSuccessContinue);
            Run("二层存在时不得误判已经返回", TestDialogExitPolicy);
            Run("视觉检测区分灰色与绿色按钮", TestSyntheticVisualDetection);
            Run("配置可保存并重新加载", TestConfigurationRoundTrip);
            Run("界面时间参数整体校验且运行快照独立", TestTimingOptions);
            Run("时间输入保留手工编辑并支持步进", TestTimingInput);
            Run("组合键解析及注册失败回滚", TestHotkeys);
            Run("倒计时动态窗口不继承上一件或重复延后", TestCountdownTracker);
            Run("冲刺交接阶段保持连续点击窗口", TestCountdownClickWindow);
            Run("数字识别拒绝价格和天小时文本", TestCountdownNegative);
            var countdownIndex = Array.IndexOf(args, "--countdown-samples");
            if (countdownIndex >= 0 && args.Length > countdownIndex + 1)
                Run("本地数字模板识别真实倒计时", () => TestCountdownSamples(args[countdownIndex + 1]));
            var uiIndex = Array.IndexOf(args, "--ui");
            if (uiIndex >= 0 && args.Length > uiIndex + 2)
                Run("主窗口实际控件保存及布局", () => TestMainForm(args[uiIndex + 1], args[uiIndex + 2]));

            var fixtureIndex = Array.IndexOf(args, "--fixtures");
            var successIndex = Array.IndexOf(args, "--success");
            if (successIndex >= 0 && args.Length > successIndex + 1)
                Run("成功界面标题和跳过跨分辨率识别", () =>
                    TestSuccessScreenshot(args[successIndex + 1],
                        fixtureIndex >= 0 ? args.Skip(fixtureIndex + 1).Take(3).ToArray() : [],
                        fixtureIndex >= 0 && args.Length > fixtureIndex + 4 ? args[fixtureIndex + 4] : null));
            if (fixtureIndex >= 0 && args.Length >= fixtureIndex + 3)
            {
                var mainScreenshot = args[fixtureIndex + 1];
                var dialogScreenshot = args[fixtureIndex + 2];
                var actualDialog = args.Length >= fixtureIndex + 4 ? args[fixtureIndex + 3] : null;
                Run("提供的商城截图可建立识别基线", () => TestProvidedScreenshots(mainScreenshot, dialogScreenshot, actualDialog));
                if (actualDialog is not null && args.Length >= fixtureIndex + 5)
                {
                    var actualConfig = args[fixtureIndex + 4];
                    Run("当前校准可识别实际二层确认按钮", () => TestActualDialogWithConfig(actualDialog, actualConfig));
                    if (args.Length >= fixtureIndex + 6)
                    {
                        var returnedMain = args[fixtureIndex + 5];
                        Run("实际二层与返回界面可可靠区分", () =>
                            TestActualDialogExitWithConfig(actualDialog, returnedMain, actualConfig));
                    }
                }
            }

            var resultIndex = Array.IndexOf(args, "--results");
            if (resultIndex >= 0 && args.Length >= resultIndex + 3)
            {
                Run("两类结果提示支持跨分辨率识别", () =>
                    TestResultScreenshots(args[resultIndex + 1], args[resultIndex + 2]));
                if (fixtureIndex >= 0 && args.Length >= fixtureIndex + 5)
                {
                    Run("当前校准可验证两类结果的原购买窗口", () =>
                        TestResultReturnWithConfig(
                            args[resultIndex + 1], args[resultIndex + 2], args[fixtureIndex + 4]));
                }
            }

            var listingIndex = Array.IndexOf(args, "--listing");
            if (listingIndex >= 0 && args.Length > listingIndex + 1)
            {
                var positives = new List<string> { args[listingIndex + 1] };
                var negatives = new List<string>();
                if (fixtureIndex >= 0)
                {
                    positives.Add(args[fixtureIndex + 3]);
                    negatives.AddRange(new[] { args[fixtureIndex + 1], args[fixtureIndex + 2], args[fixtureIndex + 5] });
                }
                if (resultIndex >= 0) positives.AddRange(args.Skip(resultIndex + 1).Take(2));
                if (successIndex >= 0) negatives.Add(args[successIndex + 1]);
                Run("挂牌剩余时间本地模板支持720p至4K且拒绝倒计时及其它页面",
                    () => TestListingDurationScreenshots(positives, negatives));
                if (fixtureIndex >= 0)
                {
                    Run("新上传挂牌页面通过原窗口返回验证",
                        () => TestResultReturnWithConfig(args[listingIndex + 1], args[listingIndex + 1],
                            args[fixtureIndex + 4]));
                    Run("挂牌截图补购并返回一级的离线回放",
                        () => TestLatePurchaseScreenshots(args[listingIndex + 1],
                            args[fixtureIndex + 5], args[fixtureIndex + 4]));
                }
            }

            Console.WriteLine($"全部通过：{_passed} 项");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void TestNormalizedCoordinates()
    {
        var normalized = new NormalizedRect(0.125, 0.25, 0.5, 0.375);
        foreach (var size in new[] { new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
        {
            var pixels = normalized.ToPixels(size);
            var restored = NormalizedRect.FromPixels(pixels, size);
            Assert(Math.Abs(restored.X - normalized.X) <= 1d / size.Width, "X 坐标缩放误差过大");
            Assert(Math.Abs(restored.Y - normalized.Y) <= 1d / size.Height, "Y 坐标缩放误差过大");
            Assert(pixels.Contains(normalized.CenterInPixels(size)), "点击中心不在目标区域内");
        }
    }

    private static void TestStableSignalGate()
    {
        var gate = new StableSignalGate(2);
        var triggers = new[] { false, true, true, true, true, false, true, true, true }
            .Count(gate.Observe);
        Assert(triggers == 2, $"期望两次状态沿触发，实际为 {triggers}");
    }

    private static void TestConfirmBurstSchedule()
    {
        Assert(
            ConfirmBurstSchedule.GetRemainingDelayMs(420, 4000) == 3580,
            "二层验证后应等待到预定冲刺时点");
        Assert(
            ConfirmBurstSchedule.GetRemainingDelayMs(4200, 4000) == 0,
            "超过冲刺时点后不应继续等待");
        Assert(
            ConfirmBurstSchedule.GetRemainingDelayMs(0, 4000) == 4000,
            "下一轮应重新等待，不继承上一轮耗时，也不能跳过等待");
    }

    private static void TestClickCadence()
    {
        var cadence = new ConfirmClickCadence(50);
        Assert(cadence.IsDue(1000), "首个点击应到期");
        cadence.RecordSent(1000);
        Assert(!cadence.IsDue(1099), "旧 50ms 配置必须受 100ms 下限限制");
        Assert(cadence.IsDue(1100), "100ms 后才允许下个点击");
        Assert(cadence.IsDue(4000), "卡顿恢复后只允许一次当前点击");
        cadence.RecordSent(4000);
        Assert(!cadence.IsDue(4000) && !cadence.IsDue(4099), "不能为了追赶时钟立即补点");
        var nextRound = new ConfirmClickCadence(200);
        nextRound.RecordSent(9000);
        Assert(!nextRound.IsDue(9199) && nextRound.IsDue(9200), "每轮独立并尊重更慢节奏");
    }

    private static void TestReturnGuard()
    {
        var guard = new PurchaseReturnGuard();
        Assert(!guard.TryBeginEscape(true, true), "一级已经出现时禁止 Esc");
        Assert(!guard.TryBeginEscape(false, false), "未知结果界面禁止 Esc");
        Assert(!guard.EscapeSent, "未验证的画面不消耗退出动作");
        Assert(guard.TryBeginEscape(false, true), "已验证原购买窗允许单次 Esc");
        Assert(!guard.TryBeginEscape(false, true), "残留二层窗不能触发第二次 Esc");
        Assert(!guard.TryBeginEscape(true, false), "返回一级后不能再次退出");
        Assert(guard.TryBeginRefresh(), "首次返回应允许一次刷新");
        Assert(!guard.TryBeginRefresh(), "重选队首失败后不能反复刷新");
    }

    private static void TestLatePurchaseGate()
    {
        var window = new LatePurchaseWindow();
        Assert(!window.TryStart(true, true, true, false, false, 0, 0), "单帧不能开启补购");
        Assert(!window.TryStart(true, false, true, false, false, 0, 20), "失去原窗口必须清除候选帧");
        Assert(!window.TryStart(true, true, true, false, false, 0, 40), "恢复后重新累计两帧");
        Assert(!window.TryStart(true, true, false, false, false, 0, 60), "灰色按钮不能补购");
        Assert(!window.TryStart(true, true, true, false, false, 0, 80), "恢复绿色后重新累计");
        Assert(window.TryStart(true, true, true, false, false, 0, 100), "两帧已验证绿色挂牌窗口开始补购");
        Assert(window.CanClick(100) && window.EndAtMs == 2100, "补购从验证时刻开始，只给两秒");

        foreach (var rejection in new[] { "unknown", "normalCountdown", "normalBurst", "alreadyClicked" })
        {
            var rejected = new LatePurchaseWindow();
            for (var i = 0; i < 4; i++)
                Assert(!rejected.TryStart(rejection != "unknown", true, true,
                    rejection == "normalCountdown", rejection == "normalBurst",
                    rejection == "alreadyClicked" ? 1 : 0, i * 20), $"{rejection} 不允许额外补购");
            Assert(!rejected.CanClick(100), $"{rejection} 不得获得点击窗口");
        }

        var tracker = new CountdownTracker();
        tracker.Observe(1, 0);
        tracker.Observe(1, 100);
        tracker.Observe(null, 1100);
        Assert(tracker.Decide(1100, 3000, 2000) == CountdownDecision.Clicking,
            "零秒后显示挂牌文本仍保留已验证的续点窗口");
        Assert(tracker.Decide(3000, 3000, 2000) == CountdownDecision.Finished,
            "正常续点结束仍遵守原有期限");
    }

    private static void TestLatePurchaseCadence()
    {
        foreach (var (interval, expected) in new[] { (100, 20), (250, 8), (1000, 2) })
        {
            var window = new LatePurchaseWindow();
            window.TryStart(true, true, true, false, false, 0, 0);
            Assert(window.TryStart(true, true, true, false, false, 0, 20), "应开始补购");
            var cadence = new ConfirmClickCadence(interval);
            var clicks = 0;
            for (var now = 20L; now <= 3020; now += 10)
            {
                // Repeated busy/unknown result frames cannot restart or extend the window.
                Assert(!window.TryStart(true, true, true, false, false, clicks, now),
                    "重复识别不得重启两秒计时");
                if (!window.CanClick(now) || !cadence.IsDue(now)) continue;
                clicks++;
                cadence.RecordSent(now);
            }
            Assert(clicks == expected, $"{interval}ms 间隔下两秒最多应发送 {expected} 次，实际 {clicks}");
            Assert(window.EndAtMs == 2020 && window.HasExpired(2020) && !window.CanClick(2020),
                "截止边界不再授权点击，且不会叠加正常冲刺时长");
        }
    }

    private static void TestLatePurchaseStop()
    {
        var window = new LatePurchaseWindow();
        window.TryStart(true, true, true, false, false, 0, 0);
        window.TryStart(true, true, true, false, false, 0, 20);
        Assert(window.CanClick(500), "停止前允许点击");
        window.Stop();
        Assert(!window.CanClick(501), "检测到结果/离开二层后停止后续输入");
        Assert(!window.TryStart(true, true, true, false, false, 0, 600) && !window.CanClick(600),
            "二层重新出现也不能重开本轮");
        Assert(window.HasExpired(2020), "提前停止后仍保留有界返回时点");
        var returned = new PurchaseReturnGuard();
        Assert(!returned.TryBeginEscape(true, false), "已自动返回一级不按 Esc");
        Assert(returned.TryBeginRefresh() && !returned.TryBeginRefresh(), "返回只刷新一次");
        var remainsInDialog = new PurchaseReturnGuard();
        Assert(remainsInDialog.TryBeginEscape(false, true) &&
               !remainsInDialog.TryBeginEscape(false, true), "补购超时留在二层只允许单次 Esc");
        var blocked = new LatePurchaseWindow();
        blocked.Stop();
        Assert(!blocked.TryStart(true, true, true, false, false, 0, 0) &&
               !blocked.TryStart(true, true, true, false, false, 0, 20),
            "结果在候选期出现时也不能再开始");
    }

    private static void TestLatePurchaseScreenshots(string listingPath, string returnedPath, string configPath)
    {
        var config = new ConfigurationStore(Path.GetDirectoryName(configPath)).Load();
        var profile = config.Calibration ?? throw new InvalidOperationException("缺少校准");
        var visual = new VisualDetector(config.Automation, profile);
        var result = new ResultVisualDetector();
        using var listing = LoadBitmap(listingPath);
        using var returned = LoadBitmap(returnedPath);
        foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
        {
            using var frame = new Bitmap(listing, size);
            using var row = VisualAnalyzer.Crop(frame, ResultVisualDetector.ListingDurationRegion);
            using var header = VisualAnalyzer.Crop(frame, profile.DialogHeaderRegion);
            using var button = VisualAnalyzer.Crop(frame, profile.ConfirmButtonRegion);
            var window = new LatePurchaseWindow();
            var visible = result.IsListingDurationVisible(row);
            var original = visual.IsOriginalDialogHeaderPresent(header, VisualAnalyzer.CreateFingerprint(header));
            var active = visual.IsConfirmButtonActive(button);
            Assert(!window.TryStart(visible, original, active, false, false, 0, 0), $"{size} 首帧不点击");
            Assert(window.TryStart(visible, original, active, false, false, 0, 20) && window.CanClick(20),
                $"{size} 挂牌时间 + 二层标题 + 绿色按钮开启补购");
            using var nextFrame = new Bitmap(returned, size);
            using var anchor = VisualAnalyzer.Crop(nextFrame, profile.MainAnchorRegion);
            using var refresh = VisualAnalyzer.Crop(nextFrame, profile.RefreshButtonRegion);
            using var nextButton = VisualAnalyzer.Crop(nextFrame, profile.ConfirmButtonRegion);
            var main = visual.IsMainScreenWithRefresh(anchor, refresh) ||
                       (visual.IsMainScreen(anchor) && !visual.IsLockedConfirmSurfacePresent(nextButton));
            Assert(main, $"{size} 应识别返回一级或空收藏");
            window.Stop();
            Assert(!window.CanClick(100), $"{size} 返回一级后立即停止补购");
        }
    }

    private static void TestListingDurationScreenshots(IEnumerable<string> positives, IEnumerable<string> negatives)
    {
        var detector = new ResultVisualDetector();
        foreach (var (paths, expected) in new[] { (positives, true), (negatives, false) })
        foreach (var path in paths)
        {
            using var source = LoadBitmap(path);
            foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
            {
                using var frame = new Bitmap(source, size);
                using var row = VisualAnalyzer.Crop(frame, ResultVisualDetector.ListingDurationRegion);
                Assert(detector.IsListingDurationVisible(row) == expected,
                    $"{Path.GetFileName(path)} {size} 挂牌时间判定应为 {expected}");
            }
        }
        using var blank = new Bitmap(160, 36);
        Assert(!detector.IsListingDurationVisible(blank), "黑屏不能认作挂牌剩余时间");
        using (var graphics = Graphics.FromImage(blank)) graphics.Clear(Color.LightGray);
        Assert(!detector.IsListingDurationVisible(blank), "亮色遮挡不能认作挂牌剩余时间");
        using var countdown = DrawCountdownText("0分3秒后解锁购买");
        Assert(!detector.IsListingDurationVisible(countdown), "正常开抢倒计时不触发退出");
        using var unrelated = DrawCountdownText("230 0分0秒");
        Assert(!detector.IsListingDurationVisible(unrelated), "价格和零秒不能代替挂牌剩余时间特征");
    }

    private static void TestSuccessReturnGuard()
    {
        var guard = new SuccessReturnGuard();
        Assert(!guard.TryBeginSkip(true, false, false), "仅成功标题不能点击");
        Assert(!guard.TryBeginSkip(false, true, false), "仅跳过文字不能点击");
        Assert(!guard.TryBeginSkip(true, true, true), "已返回一级时禁止跳过");
        Assert(!guard.SkipSent, "未验证的画面不消耗跳过动作");
        Assert(guard.TryBeginSkip(true, true, false), "成功标题和跳过同时验证后可点击");
        Assert(!guard.TryBeginSkip(true, true, false), "成功动画残留不能重复点击");
    }

    private static void TestSuccessContinue()
    {
        var guard = new SuccessReturnGuard();
        Assert(!guard.TryBeginContinue(true, false, false, false), "没有本轮跳过不能自动按空格");
        Assert(guard.TryBeginSkip(true, true, false), "先确认跳过动作");
        Assert(!guard.TryBeginContinue(false, false, false, false), "新页面未稳定不能按空格");
        Assert(!guard.TryBeginContinue(true, true, false, false), "已返回收藏时不能按空格");
        Assert(!guard.TryBeginContinue(true, false, true, false), "原购买窗不能按空格");
        Assert(!guard.TryBeginContinue(true, false, false, true), "成功展示未退出不能按空格");
        Assert(!guard.ContinueSent, "被拒绝的判断不能消耗继续动作");
        Assert(guard.TryBeginContinue(true, false, false, false), "已跳过且新页面稳定后允许单次空格");
        Assert(!guard.TryBeginContinue(true, false, false, false), "按空格后页面未退出不能反复发送");
        Assert(!guard.TryBeginSkip(true, true, false), "空格后不能再次跳过");

        var gate = new RewardContinueGate();
        for (var i = 0; i < 5; i++)
            Assert(!gate.Observe(true, true, 0, 699), "跳过后至少等待 700ms");
        Assert(!gate.Observe(true, true, 0, 700), "第一帧不能触发");
        Assert(!gate.Observe(true, true, 0, 750), "第二帧不能触发");
        Assert(gate.Observe(true, true, 0, 800), "三帧稳定后允许继续");
        Assert(!gate.Observe(true, false, 0, 850), "黑屏或空白应重置稳定计数");
        Assert(!gate.Observe(true, true, 0, 900), "黑屏恢复后必须重新累计");
        Assert(!gate.Observe(true, true, 0.1, 950), "动画变化过大时不能继续");
        Assert(!gate.Observe(false, true, 0, 1000), "返回已知页面时清除继续候选");
    }

    private static void TestCountdownSamples(string directory)
    {
        var reader = new CountdownReader();
        foreach (var (name, seconds) in new[] { ("main-11.png", 11), ("dialog-0.png", 0), ("main-422.png", 422) })
        {
            using var image = LoadBitmap(Path.Combine(directory, name));
            foreach (var scale in new[] { 0.46875, 0.625, 1.0 })
            {
                using var frame = new Bitmap(image, new Size((int)(image.Width * scale), (int)(image.Height * scale)));
                var reading = reader.Read(frame);
                Console.WriteLine($"{name} {scale}: {reader.Describe(frame)} => {reading?.Text}");
                Assert(reading?.Seconds == seconds, $"倒计时 {name} 缩放 {scale} 应识别为 {seconds}");
            }
            var learned = reader.LearnSample(image, seconds);
            Assert(learned.Count >= 2, "用户确认的截图应能录入数字模板");
            var local = new CountdownReader(learned);
            Assert(local.Read(image)?.Seconds == seconds, "录入的本地样本应可重新加载");
        }
    }

    private static void TestCountdownTracker()
    {
        var tracker = new CountdownTracker();
        Assert(!tracker.Observe(3, 0), "单帧不能确定结束时点");
        Assert(tracker.Observe(3, 100), "连续两帧应确认三秒");
        Assert(tracker.Decide(100, 3000, 2000) == CountdownDecision.Clicking, "只剩三秒时立即冲刺");
        Assert(tracker.DeadlineMs == 3000, "结束时间以首次样本计时，不加处理耗时");
        tracker.Observe(3, 400);
        Assert(tracker.DeadlineMs == 3000, "相同数字反复识别不能延后结束时点");
        Assert(tracker.Decide(500, 3000, 2000) == CountdownDecision.Clicking, "开窗花费半秒不能重新延迟");
        tracker.Observe(2, 1000);
        tracker.Observe(2, 1100);
        tracker.Observe(1, 2000);
        tracker.Observe(1, 2100);
        tracker.Observe(0, 3000);
        tracker.Observe(0, 3100);
        tracker.Observe(0, 4500);
        Assert(!tracker.Inconsistent, "重复零秒不能误判为停滞并提前终止续点");
        Assert(tracker.Decide(4999, 3000, 2000) == CountdownDecision.Clicking, "结束后两秒之前仍可续点");
        Assert(tracker.Decide(5000, 3000, 2000) == CountdownDecision.Finished, "结束后两秒准时停止");
        var next = new CountdownTracker();
        Assert(next.Decide(5100, 3000, 2000) == CountdownDecision.Unavailable, "下一商品不能继承旧时点");
        var closeListing = new CountdownTracker();
        closeListing.Observe(3, 5100);
        closeListing.Observe(3, 5200);
        Assert(closeListing.Decide(5200, 3000, 2000) == CountdownDecision.Clicking &&
               closeListing.DeadlineMs == 8100, "上一件结束后下一件仅剩三秒时应立即开始，不再等固定延迟");
        next.Observe(0, 5100);
        next.Observe(0, 5200);
        Assert(next.DeadlineMs is null, "新商品只有零秒不能猜测实际开放时间");
        next.Observe(5, 6000);
        next.Observe(5, 6100);
        Assert(next.Decide(6100, 3000, 2000) == CountdownDecision.Waiting, "五秒时应先等待");
        next.Observe(3, 8000);
        next.Observe(3, 8100);
        Assert(next.Decide(8100, 3000, 2000) == CountdownDecision.Clicking, "到最后三秒再开始");
        next.Observe(5, 8200);
        next.Observe(5, 8300);
        Assert(next.Inconsistent && next.Decide(8300, 3000, 2000) == CountdownDecision.Unavailable,
            "倒计时倒增时不能继续点击");
        var frozen = new CountdownTracker();
        frozen.Observe(3, 0);
        frozen.Observe(3, 100);
        Assert(frozen.Decide(701, 3000, 2000) == CountdownDecision.Unavailable, "过期读数不能一直授权输入");
        frozen.Observe(3, 1600);
        Assert(frozen.Inconsistent, "数字冻结也应停止购买");
    }

    private static void TestCountdownClickWindow()
    {
        var window = new CountdownClickWindow();
        Assert(!window.Observe(CountdownDecision.Waiting, 1000, 5000, 2000),
            "提前窗口前不能点击");
        Assert(window.Observe(CountdownDecision.Clicking, 2000, 5000, 2000),
            "进入提前窗口后应开始点击");
        Assert(window.IsLatched && window.EndAtMs == 7000,
            "点击窗口结束点应固定在开放时点加续点时长");
        Assert(window.Observe(CountdownDecision.Unavailable, 5001, 5000, 2000),
            "交接瞬间识别丢帧不能造成点击间隙");
        Assert(window.Observe(CountdownDecision.Finished, 6999, 5000, 2000),
            "结束前仍应继续点击");
        Assert(!window.Observe(CountdownDecision.Finished, 7000, 5000, 2000) &&
               window.HasFinished(7000),
            "达到续点结束点后应停止点击");
        var noWindow = new CountdownClickWindow();
        Assert(!noWindow.Observe(CountdownDecision.Unavailable, 5000, null, 2000),
            "没有可信开放时点不能因为丢帧而开始点击");
    }

    private static void TestCountdownNegative()
    {
        var reader = new CountdownReader();
        foreach (var text in new[] { "230", "剩余2天23小时", "剩余23小时29分", "0分99秒", "购买人数过多，请重试" })
        {
            using var image = DrawCountdownText(text);
            Assert(reader.Read(image) is null, $"不应把 {text} 当成倒计时");
        }
        using var blank = new Bitmap(320, 50);
        Assert(reader.Read(blank) is null, "黑屏不能被识别成零秒");
        using var unknownPrefix = DrawCountdownText("?1分3秒");
        Assert(reader.Read(unknownPrefix) is null, "不能忽略无法识别的首位数字而读出错误分钟数");
        foreach (var seconds in Enumerable.Range(0, 10))
        {
            using var image = DrawCountdownText($"0分{seconds}秒后解锁购买");
            var reading = reader.Read(image);
            Assert(reading?.Seconds == seconds, $"字体模板应支持数字 {seconds}，实际 {reader.Describe(image)}");
        }
    }

    private static Bitmap DrawCountdownText(string text)
    {
        var image = new Bitmap(550, 70);
        using var graphics = Graphics.FromImage(image);
        using var font = new Font("Segoe UI", 32, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.Clear(Color.Black);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(text, font, Brushes.White, 4, 4, StringFormat.GenericTypographic);
        return image;
    }
    private static void TestDialogExitPolicy()
    {
        Assert(
            !DialogExitPolicy.IsExitCandidate(true, true, 1000),
            "二层确认区域仍存在时，即使一级锚点误报也不能退出冲刺");
        Assert(
            !DialogExitPolicy.IsExitCandidate(true, false, 299),
            "确认区域短暂消失时不能立即判定二层已经关闭");
        Assert(
            DialogExitPolicy.IsExitCandidate(true, false, 300),
            "确认区域持续消失且一级界面成立后应允许退出冲刺");
        Assert(
            !DialogExitPolicy.IsExitCandidate(false, false, 1000),
            "未知界面不能冒充已经返回的一级界面");
    }

    private static void TestConfirmClickLimit()
    {
        Assert(!ConfirmBurstSchedule.HasReachedClickLimit(4999, 3000, 2000), "额外两秒结束前仍可提交");
        Assert(ConfirmBurstSchedule.HasReachedClickLimit(5000, 3000, 2000), "基础三秒加续点两秒必须在五秒停止");
        Assert(ConfirmBurstSchedule.TotalDurationMs(int.MaxValue, int.MaxValue) == (long)int.MaxValue + 2000,
            "大时长必须使用 long 相加，不能溢出后提前停止");
        Assert(!ConfirmBurstSchedule.HasReachedClickLimit(4999, 5000), "自定义五秒冲刺不能被截断到三秒");
        Assert(ConfirmBurstSchedule.HasReachedClickLimit(5000, 5000), "五秒冲刺设置应在五秒结束");
        Assert(ConfirmBurstSchedule.TotalDurationMs(1000, 500) == 1500, "两项时长修改必须同步进入后端");
        Assert(!ConfirmBurstSchedule.HasReachedClickLimit(2999, 3000), "上限之前仍可提交");
        Assert(ConfirmBurstSchedule.HasReachedClickLimit(3000, 3000), "到达 3 秒必须停止");
        Assert(!ConfirmBurstSchedule.HasReachedClickLimit(3000, int.MaxValue), "不再人为限制三秒上限");
        Assert(ConfirmBurstSchedule.HasReachedClickLimit(500, -1), "异常配置应限制到有效范围");
        Assert(ConfirmBurstSchedule.HasReachedClickLimit(1000, 1000), "更短的配置时长应生效");
        for (var elapsed = 3000; elapsed <= 8000; elapsed += 50)
        {
            Assert(ConfirmBurstSchedule.HasReachedClickLimit(elapsed, 3000), "等待返回期间不得重开点击窗口");
        }
    }

    private static void TestSyntheticVisualDetection()
    {
        using var inactive = DrawButton(Color.FromArgb(49, 55, 58), Color.FromArgb(100, 105, 108));
        using var active = DrawButton(Color.FromArgb(17, 32, 31), Color.FromArgb(25, 225, 151));
        using var blank = new Bitmap(320, 90, PixelFormat.Format24bppRgb);
        using var header = DrawHeader();

        var profile = new CalibrationProfile
        {
            FirstItemRegion = new NormalizedRect(0.05, 0.2, 0.3, 0.2),
            PrimaryButtonRegion = new NormalizedRect(0.75, 0.82, 0.2, 0.08),
            RefreshButtonRegion = new NormalizedRect(0.7, 0.15, 0.04, 0.07),
            ConfirmButtonRegion = new NormalizedRect(0.55, 0.65, 0.23, 0.09),
            DialogHeaderRegion = new NormalizedRect(0.12, 0.09, 0.27, 0.08),
            MainAnchorRegion = new NormalizedRect(0.4, 0.14, 0.35, 0.10),
            PrimaryInactiveFingerprint = VisualAnalyzer.CreateFingerprint(inactive),
            RefreshFingerprint = VisualAnalyzer.CreateFingerprint(inactive),
            MainAnchorFingerprint = VisualAnalyzer.CreateFingerprint(inactive),
            DialogHeaderFingerprint = VisualAnalyzer.CreateFingerprint(header)
        };
        var settings = new AutomationSettings
        {
            PrimaryGreenRatioMin = 0.004,
            ConfirmGreenRatioMin = 0.004,
            PrimaryDifferenceMin = 0.025
        };
        var detector = new VisualDetector(settings, profile);

        Assert(!detector.IsPrimaryActive(inactive), "灰色基线不能被识别为可购买");
        Assert(detector.IsPrimaryActive(active), "绿色价格按钮应被识别为可购买");
        Assert(detector.IsConfirmButtonActive(active), "弹窗锁定后绿色确认按钮应保持有效");
        Assert(!detector.IsConfirmButtonActive(inactive), "弹窗锁定后灰色按钮仍不能触发点击");
        Assert(detector.IsLockedConfirmSurfacePresent(active), "锁定后二层绿色按钮区域应允许持续提交");
        Assert(detector.IsLockedConfirmSurfacePresent(inactive), "锁定后按钮短暂变灰不应中断持续提交");
        Assert(detector.IsMainScreenWithRefresh(inactive, inactive),
            "一级锚点及刷新按钮都匹配时，应优先停止点击，不受其它区域纹理影响");
        Assert(!detector.IsMainScreenWithRefresh(inactive, blank), "一级锚点单独匹配不能作为强退出证据");
        Assert(!detector.IsLockedConfirmSurfacePresent(blank), "锁定后空白或黑屏区域不能发送点击");
        Assert(detector.IsConfirmReady(active, header), "弹窗标题与绿色确认按钮组合应通过验证");
        Assert(!detector.IsConfirmReady(inactive, header), "灰色按钮不能通过二层确认验证");
    }

    private static void TestConfigurationRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeltaMarketAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(directory);
            var expected = new AppConfig { TargetProcessName = "game", StartPauseHotkey = "F7" };
            store.Save(expected);
            var actual = store.Load();
            Assert(actual.TargetProcessName == "game" && actual.StartPauseHotkey == "F7", "配置往返结果不一致");

            File.WriteAllText(store.ConfigPath, """
                {
                  "SchemaVersion": 2,
                  "Automation": {
                    "PollIntervalMs": 25
                  }
                }
                """);
            var upgraded = store.Load();
            Assert(upgraded.Automation.PollIntervalMs == 25, "旧配置中的现有设置应保留");
            Assert(upgraded.SchemaVersion == 11, "旧配置应升级到当前版本");
            Assert(upgraded.Automation.CountdownDriven, "新版默认使用倒计时模式");
            Assert(upgraded.Automation.ConfirmBurstStartDelayMs == 4000, "旧配置应补入最后一秒确认冲刺默认值");
            Assert(upgraded.Automation.ConfirmBurstDurationMs == 3000, "连续点击应覆盖最后一秒和结束后两秒");
            Assert(upgraded.Automation.ConfirmClickIntervalMs == 100, "旧配置应升级到 100ms 间隔");
            Assert(upgraded.Automation.ResultWaitTimeoutMs == 15000, "未知结果默认等待 15 秒");
            Assert(upgraded.Automation.AutomaticReturnTimeoutMs == 4000, "旧配置应补入自动返回等待默认值");
            Assert(upgraded.Automation.ConfirmInputDrainMs == 100, "旧配置应补入输入排空等待默认值");
            Assert(upgraded.Automation.PostConfirmSettleMs == 75, "旧配置应升级主界面稳定等待默认值");
            Assert(upgraded.Automation.RefreshDelayMs == 150, "刷新等待默认应为 150ms");
            Assert(upgraded.Automation.ConfirmExtraDurationMs == 2000, "默认增加两秒续点");
            Assert(upgraded.Automation.MainScreenStableFramesRequired == 3, "旧配置应补入主界面稳定帧数默认值");
            Assert(upgraded.Automation.PeriodicRefreshIntervalMs == 1000, "旧配置应补入定时刷新间隔默认值");
            Assert(upgraded.Automation.ConfirmClicksPerPacket == 4, "旧配置应补入二层短点击包默认次数");

            expected.SchemaVersion = 9;
            expected.EmergencyStopHotkey = "F12";
            expected.Automation.RefreshDelayMs = 225;
            expected.Automation.PostConfirmSettleMs = 125;
            store.Save(expected);
            var custom = store.Load();
            Assert(custom.Automation.RefreshDelayMs == 225 && custom.Automation.PostConfirmSettleMs == 125,
                "升级不能覆盖非默认的自定义时间");
            Assert(custom.EmergencyStopHotkey == "Ctrl+Shift+F9", "旧保留键应迁移到可用默认组合");
            custom.Automation.ConfirmExtraDurationMs = 500;
            store.Save(custom);
            Assert(store.Load().Automation.ConfirmExtraDurationMs == 500, "新版自定义额外续点不能被默认值覆盖");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void TestTimingOptions()
    {
        var config = new AppConfig();
        var snapshot = ConfigurationStore.Snapshot(config);
        var values = TimingOptions.All.ToDictionary(o => o.Id, o => o.Read(config.Automation).ToString());
        foreach (var option in TimingOptions.All) values[option.Id] = option.Maximum.ToString();
        Assert(TimingOptions.TryApply(config.Automation, values, out _), "所有上限应可设置");
        foreach (var option in TimingOptions.All)
            Assert(option.Read(config.Automation) == option.Maximum, $"{option.Id} 未应用到后端");
        Assert(snapshot.Automation.ConfirmBurstStartDelayMs == 4000, "正在运行的快照不能随 UI 修改");
        values[TimingOptions.StartDelay.Id] = "1234";
        values[TimingOptions.ClickInterval.Id] = "99";
        Assert(!TimingOptions.TryApply(config.Automation, values, out _), "越界参数应拒绝");
        Assert(config.Automation.ConfirmBurstStartDelayMs == 6000, "验证失败不得部分应用");
        values[TimingOptions.ClickInterval.Id] = "125";
        Assert(TimingOptions.TryApply(config.Automation, values, out _), "非步进倍数的合法手工值应接受");
        Assert(config.Automation.ConfirmBurstStartDelayMs == 1234, "手工值不能被步进值取代");
    }

    private static void TestTimingInput()
    {
        using var input = new TimingValueInput { Minimum = 100, Maximum = 1000, Increment = 25, Value = 100 };
        input.Text = "225";
        Assert(input.TryCommit() && input.Value == 225 && input.Text == "225", "手动编辑必须保留");
        input.UpButton();
        Assert(input.Value == 250, "步进必须基于新值");
        input.Text = "99";
        Assert(!input.TryCommit() && input.Text == "99", "越界输入不可悄悄还原");
        _ = input.Value;
        Assert(input.Text == "99", "读取 Value 也不能重写无效文本");
        input.Text = "";
        Assert(!input.TryCommit() && input.Text == "", "清空后应允许继续编辑");
        input.Text = "150";
        Assert(input.TryCommit() && input.Value == 150, "修正输入后应正常提交");
    }

    private static void TestHotkeys()
    {
        Assert(HotkeyChord.TryParse("shift+control+f5", out var chord, out _) &&
               chord.Name == "Ctrl+Shift+F5" && chord.Modifiers == 6, "组合键应规范化并映射修饰键");
        Assert(HotkeyChord.TryParse("Alt+1", out var number, out _) && number.Key == System.Windows.Forms.Keys.D1,
            "数字组合键应正确映射");
        Assert(HotkeyChord.FormatKeyData(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.K) == "Ctrl+K",
            "实际键盘输入应正确记录");
        foreach (var invalid in new[] { "A", "F10", "F12", "Ctrl+F12", "Ctrl+Ctrl+A", "Ctrl+", "Alt+F4", "Ctrl+Alt+Delete" })
            Assert(!HotkeyChord.TryParse(invalid, out _, out _), $"不应接受 {invalid}");
        var registered = new Dictionary<int, HotkeyChord>();
        var pair = new HotkeyPairRegistration(
            (id, value) => value.Name != "Ctrl+Q" && registered.TryAdd(id, value),
            id => registered.Remove(id));
        Assert(pair.TryApply("F5", "F9", out _) && registered.Count == 2, "两项热键应同时注册");
        Assert(!pair.TryApply("Ctrl+K", "Ctrl+Q", out _), "被占用热键应报错");
        Assert(pair.IsRegistered && registered[101].Name == "F5" && registered[102].Name == "F9",
            "第二项失败必须恢复原来的完整热键组");
        Assert(!pair.TryApply("Ctrl+K", "Ctrl+K", out _) && registered.Count == 2,
            "重复组合必须在注销旧键之前拒绝");
        Assert(pair.TryApply("Ctrl+K", "Alt+1", out _), "可用组合应成功应用");
        pair.Unregister();
        Assert(registered.Count == 0 && !pair.IsRegistered, "退出时应注销全部热键");
    }

    private static void TestMainForm(string assemblyPath, string outputDirectory)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        Environment.SetEnvironmentVariable("DMA_UI_PREVIEW", "1");
        Directory.CreateDirectory(outputDirectory);
        var store = new ConfigurationStore(Path.Combine(outputDirectory, "test-config"));
        var type = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(assemblyPath))
            .GetType("DeltaMarketAssistant.MainForm", true)!;
        using var form = (System.Windows.Forms.Form)Activator.CreateInstance(
            type, flags, null, [store], null)!;
        var inputs = (Dictionary<string, TimingValueInput>)type.GetField("_timingInputs", flags)!.GetValue(form)!;
        var save = type.GetMethod("SaveTimingInputs", flags)!;
        inputs[TimingOptions.RefreshInterval.Id].Text = "1750";
        inputs[TimingOptions.StartDelay.Id].Text = "3800";
        inputs[TimingOptions.ClickInterval.Id].Text = "175";
        inputs[TimingOptions.BurstDuration.Id].Text = "5000";
        inputs[TimingOptions.CountdownLead.Id].Text = "2500";
        inputs[TimingOptions.CountdownTail.Id].Text = "1800";
        Assert((bool)save.Invoke(form, [false])!, "窗口保存有效输入应成功");
        var saved = store.Load();
        Assert(saved.Automation.PeriodicRefreshIntervalMs == 1750 &&
               saved.Automation.ConfirmBurstStartDelayMs == 3800 &&
               saved.Automation.ConfirmClickIntervalMs == 175 &&
               saved.Automation.ConfirmBurstDurationMs == 5000 &&
               saved.Automation.CountdownLeadMs == 2500 &&
               saved.Automation.CountdownTailMs == 1800, "实际 UI 输入必须完整落盘");
        inputs[TimingOptions.ClickInterval.Id].Text = "20";
        Assert(!(bool)save.Invoke(form, [false])!, "窗口必须拒绝越界输入");
        Assert(inputs[TimingOptions.ClickInterval.Id].Text == "20" &&
               store.Load().Automation.ConfirmClickIntervalMs == 175, "不能回退输入或污染已保存配置");
        inputs[TimingOptions.ClickInterval.Id].Text = "175";
        form.ShowInTaskbar = false;
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.Show();
        var modeInput = (System.Windows.Forms.ComboBox)type.GetField("_timingMode", flags)!.GetValue(form)!;
        foreach (var mode in new[] { 0, 1 })
        {
        modeInput.SelectedIndex = mode;
        Assert((bool)save.Invoke(form, [true])!, "计时模式应可保存");
        Assert(store.Load().Automation.CountdownDriven == (mode == 0), "模式选择必须同步到配置");
        foreach (var size in new[] { new Size(1080, 760), new Size(920, 650) })
        {
            form.Size = size;
            form.CreateControl();
            form.PerformLayout();
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(outputDirectory, $"main-mode{mode}-{size.Width}x{size.Height}.png"), ImageFormat.Png);
            foreach (var input in inputs.Values)
            {
                if (!input.Visible) continue;
                var bounds = form.RectangleToClient(input.RectangleToScreen(input.ClientRectangle));
                Assert(form.ClientRectangle.Contains(bounds), $"参数 {input.Name} 超出窗口边界");
            }
        }
        }
        type.GetField("_allowExit", flags)!.SetValue(form, true);
        form.Close();
        using var countdownImage = DrawCountdownText("0分3秒后解锁购买");
        var sampleType = type.Assembly.GetType("DeltaMarketAssistant.CountdownSampleForm", true)!;
        using var sampleForm = (System.Windows.Forms.Form)Activator.CreateInstance(
            sampleType, [countdownImage, new CountdownReading(3, "0分3秒", 1)])!;
        sampleForm.ShowInTaskbar = false;
        sampleForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        sampleForm.Location = new Point(-10000, -10000);
        sampleForm.Show();
        System.Windows.Forms.Application.DoEvents();
        Assert((int)sampleType.GetProperty("TotalSeconds")!.GetValue(sampleForm)! == 3,
            "数字样本确认窗口应显示当前识别值");
        using (var bitmap = new Bitmap(sampleForm.Width, sampleForm.Height))
        {
            sampleForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(outputDirectory, "countdown-sample.png"), ImageFormat.Png);
        }
        sampleForm.Close();
        Environment.SetEnvironmentVariable("DMA_UI_PREVIEW", null);
    }

    private static void TestProvidedScreenshots(string mainPath, string dialogPath, string? actualDialogPath)
    {
        using var main = LoadBitmap(mainPath);
        using var dialog = LoadBitmap(dialogPath);
        var primaryRegion = new NormalizedRect(0.772, 0.835, 0.155, 0.054);
        var refreshRegion = new NormalizedRect(0.704, 0.161, 0.024, 0.044);
        var confirmRegion = new NormalizedRect(0.554, 0.658, 0.224, 0.080);
        var headerRegion = new NormalizedRect(0.12, 0.09, 0.27, 0.08);
        var mainAnchorRegion = CalibrationProfile.DeriveMainAnchorRegion(refreshRegion);

        using var inactivePrimary = VisualAnalyzer.Crop(main, primaryRegion);
        using var refresh = VisualAnalyzer.Crop(main, refreshRegion);
        using var confirm = VisualAnalyzer.Crop(dialog, confirmRegion);
        using var header = VisualAnalyzer.Crop(dialog, headerRegion);
        using var mainAnchor = VisualAnalyzer.Crop(main, mainAnchorRegion);
        using var dialogAtMainAnchor = VisualAnalyzer.Crop(dialog, mainAnchorRegion);
        var profile = new CalibrationProfile
        {
            FirstItemRegion = new NormalizedRect(0.047, 0.225, 0.337, 0.19),
            PrimaryButtonRegion = primaryRegion,
            RefreshButtonRegion = refreshRegion,
            ConfirmButtonRegion = confirmRegion,
            DialogHeaderRegion = headerRegion,
            MainAnchorRegion = mainAnchorRegion,
            PrimaryInactiveFingerprint = VisualAnalyzer.CreateFingerprint(inactivePrimary),
            RefreshFingerprint = VisualAnalyzer.CreateFingerprint(refresh),
            MainAnchorFingerprint = VisualAnalyzer.CreateFingerprint(mainAnchor),
            DialogHeaderFingerprint = VisualAnalyzer.CreateFingerprint(header)
        };
        var detector = new VisualDetector(new AutomationSettings(), profile);

        Assert(profile.IsComplete, "截图生成的校准资料应完整");
        Assert(!detector.IsPrimaryActive(inactivePrimary), "截图中的“公示中”按钮不能触发购买");
        Assert(detector.IsConfirmReady(confirm, header), "截图中的绿色二层确认区应被识别");
        Assert(detector.IsMainScreen(mainAnchor), "主界面宽锚点应通过验证");
        var dialogMainDifference = VisualAnalyzer.FingerprintDifference(
            dialogAtMainAnchor,
            profile.MainAnchorFingerprint);
        Assert(!detector.IsMainScreen(dialogAtMainAnchor),
            $"二层弹窗不能冒充商城主界面（当前差异 {dialogMainDifference:F4}）");

        if (!string.IsNullOrWhiteSpace(actualDialogPath))
        {
            using var actualDialog = LoadBitmap(actualDialogPath);
            using var actualDialogAtMainAnchor = VisualAnalyzer.Crop(actualDialog, mainAnchorRegion);
            var actualDifference = VisualAnalyzer.FingerprintDifference(
                actualDialogAtMainAnchor,
                profile.MainAnchorFingerprint);
            Assert(!detector.IsMainScreen(actualDialogAtMainAnchor),
                $"实际诊断弹窗不能冒充商城主界面（当前差异 {actualDifference:F4}）");
        }

        foreach (var size in new[] { new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
        {
            using var scaledMain = new Bitmap(main, size);
            using var scaledMainAnchor = VisualAnalyzer.Crop(scaledMain, mainAnchorRegion);
            using var scaledDialog = new Bitmap(dialog, size);
            using var scaledConfirm = VisualAnalyzer.Crop(scaledDialog, confirmRegion);
            using var scaledHeader = VisualAnalyzer.Crop(scaledDialog, headerRegion);
            var mainScaleDifference = VisualAnalyzer.FingerprintDifference(
                scaledMainAnchor,
                profile.MainAnchorFingerprint);
            Assert(detector.IsMainScreen(scaledMainAnchor),
                $"{size.Width}x{size.Height} 缩放后主界面锚点应保持识别（差异 {mainScaleDifference:F4}）");
            Assert(detector.IsConfirmReady(scaledConfirm, scaledHeader), $"{size.Width}x{size.Height} 缩放后应保持二层识别");
        }
    }

    private static void TestActualDialogWithConfig(string dialogPath, string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException("配置目录无效");
        var config = new ConfigurationStore(directory).Load();
        var calibration = config.Calibration ?? throw new InvalidOperationException("当前配置缺少校准");
        Assert(calibration.IsComplete, "当前配置应包含完整校准");

        using var dialog = LoadBitmap(dialogPath);
        using var confirm = VisualAnalyzer.Crop(dialog, calibration.ConfirmButtonRegion);
        using var mainAnchor = VisualAnalyzer.Crop(dialog, calibration.MainAnchorRegion);
        var detector = new VisualDetector(config.Automation, calibration);
        var confirmStats = VisualAnalyzer.Analyze(confirm);
        Assert(
            detector.IsConfirmButtonActive(confirm),
            $"实际二层绿色确认按钮应在锁定会话后保持可点击（绿色比例 {confirmStats.GreenRatio:F5}）");
        Assert(detector.IsLockedConfirmSurfacePresent(confirm), "实际二层确认区域应保持会话锁定");
        Assert(!detector.IsMainScreen(mainAnchor), "实际二层弹窗不能被识别为一级商城");
    }

    private static void TestActualDialogExitWithConfig(
        string dialogPath,
        string returnedMainPath,
        string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException("配置目录无效");
        var config = new ConfigurationStore(directory).Load();
        var calibration = config.Calibration ?? throw new InvalidOperationException("当前配置缺少校准");
        var detector = new VisualDetector(config.Automation, calibration);

        using var dialog = LoadBitmap(dialogPath);
        using var returnedMain = LoadBitmap(returnedMainPath);
        using var dialogConfirm = VisualAnalyzer.Crop(dialog, calibration.ConfirmButtonRegion);
        using var lockedHeader = VisualAnalyzer.Crop(dialog, calibration.DialogHeaderRegion);
        using var dialogAnchor = VisualAnalyzer.Crop(dialog, calibration.MainAnchorRegion);
        using var dialogRefresh = VisualAnalyzer.Crop(dialog, calibration.RefreshButtonRegion);
        using var returnedConfirm = VisualAnalyzer.Crop(returnedMain, calibration.ConfirmButtonRegion);
        using var returnedAnchor = VisualAnalyzer.Crop(returnedMain, calibration.MainAnchorRegion);
        using var returnedRefresh = VisualAnalyzer.Crop(returnedMain, calibration.RefreshButtonRegion);
        using var returnedHeader = VisualAnalyzer.Crop(returnedMain, calibration.DialogHeaderRegion);
        using var returnedBack = VisualAnalyzer.Crop(returnedMain, ResultVisualDetector.BackRegion);
        using var returnedToast = VisualAnalyzer.Crop(returnedMain, ResultVisualDetector.ToastRegion);
        var results = new ResultVisualDetector();
        var originalHeaderPresent = detector.IsOriginalDialogHeaderPresent(
            returnedHeader, VisualAnalyzer.CreateFingerprint(lockedHeader));
        Assert(!(originalHeaderPresent && results.IsDialogBackVisible(returnedBack)),
            "返回一级后不能误认原购买窗并发送 Esc");
        Assert(results.Detect(returnedToast) == PurchaseResult.Unknown,
            "一级商城或已售出提示不能误判为两类抢购结果");

        Assert(detector.IsLockedConfirmSurfacePresent(dialogConfirm), "实际二层界面的确认区域应存在");
        Assert(!detector.IsMainScreenWithRefresh(dialogAnchor, dialogRefresh),
            "实际二层弹窗不能误报一级商城刷新按钮");
        if (VisualAnalyzer.Analyze(returnedRefresh).HasDetail)
        {
            Assert(detector.IsMainScreenWithRefresh(returnedAnchor, returnedRefresh),
                "有刷新按钮的一级商城应立即阻止继续购买点击");
        }
        Assert(!detector.IsLockedConfirmSurfacePresent(returnedConfirm), "游戏返回后确认区域应消失");
        Assert(detector.IsMainScreen(returnedAnchor), "游戏返回后的一级界面应通过主界面验证");
        Assert(
            !DialogExitPolicy.IsExitCandidate(true, true, 1000),
            "实际二层确认区域存在时不能被一级锚点误报关闭");
        Assert(
            DialogExitPolicy.IsExitCandidate(true, false, 300),
            "实际返回一级界面后应退出冲刺");
    }

    private static void TestResultScreenshots(string queuePath, string rollPath)
    {
        var detector = new ResultVisualDetector();
        foreach (var (path, expected) in new[]
                 { (queuePath, PurchaseResult.QueueFull), (rollPath, PurchaseResult.RollFailed) })
        {
            using var source = LoadBitmap(path);
            foreach (var size in new[] { new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
            {
                using var scaled = new Bitmap(source, size);
                using var toast = VisualAnalyzer.Crop(scaled, ResultVisualDetector.ToastRegion);
                using var back = VisualAnalyzer.Crop(scaled, ResultVisualDetector.BackRegion);
                Assert(detector.Detect(toast) == expected, $"{size} 应识别结果 {expected}");
                Assert(detector.IsDialogBackVisible(back), $"{size} 应识别原购买窗底部返回提示");

                using (var graphics = Graphics.FromImage(scaled))
                {
                    // Random roll numbers are outside the static prefix used by the matcher.
                    graphics.FillRectangle(Brushes.Black,
                        new Rectangle((int)(940 / 2048d * size.Width), (int)(165 / 1152d * size.Height),
                            (int)(400 / 2048d * size.Width), (int)(30 / 1152d * size.Height)));
                }
                if (expected == PurchaseResult.RollFailed)
                {
                    using var modifiedToast = VisualAnalyzer.Crop(scaled, ResultVisualDetector.ToastRegion);
                    Assert(detector.Detect(modifiedToast) == expected, "随机点数改变不能影响未抢购成功判定");
                }
            }
        }

        using var unknown = new Bitmap(2048, 1152);
        using (var graphics = Graphics.FromImage(unknown))
        using (var font = new Font("Microsoft YaHei UI", 14f, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.FromArgb(16, 23, 29));
            graphics.DrawString("购买人数过多，请重试", font, Brushes.LightGray, 914, 169);
        }
        using var busyToast = VisualAnalyzer.Crop(unknown, ResultVisualDetector.ToastRegion);
        Assert(detector.Detect(busyToast) == PurchaseResult.Unknown, "繁忙重试不是队列已满或 roll 失败");
        using var blank = new Bitmap(740, 52);
        Assert(detector.Detect(blank) == PurchaseResult.Unknown, "黑屏和未知结果不能分类为终态");
        Assert(!detector.IsDialogBackVisible(blank), "没有底部返回提示时不能判定可退出原购买窗");
    }

    private static void TestSuccessScreenshot(string successPath, string[] negativePaths, string? configPath)
    {
        var detector = new ResultVisualDetector();
        var config = configPath is null ? null : new ConfigurationStore(Path.GetDirectoryName(configPath)).Load();
        using var source = LoadBitmap(successPath);
        foreach (var size in new[] { new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
        {
            using var frame = new Bitmap(source, size);
            using var title = VisualAnalyzer.Crop(frame, ResultVisualDetector.SuccessTitleRegion);
            using var skip = VisualAnalyzer.Crop(frame, ResultVisualDetector.SuccessSkipRegion);
            Assert(detector.IsSuccessTitleVisible(title), $"{size} 应识别获得枪械外观标题");
            Assert(detector.IsSuccessSkipVisible(skip), $"{size} 应识别跳过按钮");
            if (config?.Calibration is { } profile)
            {
                var visual = new VisualDetector(config.Automation, profile);
                using var anchor = VisualAnalyzer.Crop(frame, profile.MainAnchorRegion);
                using var refresh = VisualAnalyzer.Crop(frame, profile.RefreshButtonRegion);
                Assert(!visual.IsMainScreenWithRefresh(anchor, refresh),
                    $"{size} 成功画面不能冒充一级商城而跳过成功处理");
            }
            var guard = new SuccessReturnGuard();
            Assert(guard.TryBeginSkip(detector.IsSuccessTitleVisible(title), detector.IsSuccessSkipVisible(skip), false),
                $"{size} 应允许单次跳过");
            Assert(!guard.TryBeginSkip(true, true, false), "重复画面不能再点跳过");
            using var apply = VisualAnalyzer.Crop(frame, new NormalizedRect(1740 / 2048d, 921 / 1152d, 110 / 2048d, 34 / 1152d));
            Assert(!detector.IsSuccessSkipVisible(apply), "应用外观不能被识别为跳过");
        }
        foreach (var path in negativePaths)
        {
            using var frame = LoadBitmap(path);
            using var title = VisualAnalyzer.Crop(frame, ResultVisualDetector.SuccessTitleRegion);
            using var skip = VisualAnalyzer.Crop(frame, ResultVisualDetector.SuccessSkipRegion);
            Assert(!detector.IsSuccessTitleVisible(title) && !detector.IsSuccessSkipVisible(skip),
                "一级商城和普通二层窗口不能冒充成功画面");
        }
        using var blank = new Bitmap(260, 48);
        Assert(!detector.IsSuccessTitleVisible(blank) && !detector.IsSuccessSkipVisible(blank),
            "空白画面不能触发跳过");
    }

    private static void TestResultReturnWithConfig(string queuePath, string rollPath, string configPath)
    {
        var config = new ConfigurationStore(Path.GetDirectoryName(configPath)).Load();
        var profile = config.Calibration ?? throw new InvalidOperationException("测试缺少校准资料");
        var visual = new VisualDetector(config.Automation, profile);
        var results = new ResultVisualDetector();
        using var queue = LoadBitmap(queuePath);
        using var lockedHeader = VisualAnalyzer.Crop(queue, profile.DialogHeaderRegion);
        var fingerprint = VisualAnalyzer.CreateFingerprint(lockedHeader);
        foreach (var path in new[] { queuePath, rollPath })
        {
            using var source = LoadBitmap(path);
            foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
            {
                using var frame = new Bitmap(source, size);
                using var header = VisualAnalyzer.Crop(frame, profile.DialogHeaderRegion);
                using var back = VisualAnalyzer.Crop(frame, ResultVisualDetector.BackRegion);
                using var main = VisualAnalyzer.Crop(frame, profile.MainAnchorRegion);
                using var refresh = VisualAnalyzer.Crop(frame, profile.RefreshButtonRegion);
                var originalDialog = visual.IsOriginalDialogHeaderPresent(header, fingerprint) &&
                                     results.IsDialogBackVisible(back);
                var mainVisible = visual.IsMainScreenWithRefresh(main, refresh);
                var guard = new PurchaseReturnGuard();
                Assert(guard.TryBeginEscape(mainVisible, originalDialog),
                    $"{Path.GetFileName(path)} {size} 的已知结果应允许单次返回" +
                    $"（标题={visual.IsOriginalDialogHeaderPresent(header, fingerprint)}，返回={results.IsDialogBackVisible(back)}，一级={mainVisible}）");
                Assert(!guard.TryBeginEscape(mainVisible, originalDialog), "相同结果不能再次退出");
            }
        }
    }

    private static Bitmap DrawButton(Color background, Color accent)
    {
        var bitmap = new Bitmap(320, 90, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(background);
        using var pen = new Pen(accent, 5);
        graphics.DrawRectangle(pen, 3, 3, bitmap.Width - 7, bitmap.Height - 7);
        using var brush = new SolidBrush(accent);
        graphics.FillRectangle(brush, 125, 30, 70, 30);
        return bitmap;
    }

    private static Bitmap DrawHeader()
    {
        var bitmap = new Bitmap(420, 100, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(17, 25, 32));
        using var pen = new Pen(Color.FromArgb(165, 175, 178), 4);
        graphics.DrawLine(pen, 0, 84, bitmap.Width, 84);
        for (var x = 28; x < 220; x += 22)
        {
            graphics.FillRectangle(Brushes.LightGray, x, 24, 12, 38);
        }
        return bitmap;
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var source = Image.FromFile(path);
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, result.Width, result.Height),
            new Rectangle(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);
        return result;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"通过：{name}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("测试失败：" + message);
        }
    }
}
