using System.Drawing;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public enum PurchaseResult
{
    Unknown,
    QueueFull,
    RollFailed
}

public sealed class ResultVisualDetector
{
    public static readonly NormalizedRect ToastRegion = new(0.32, 0.135, 0.36, 0.045);
    public static readonly NormalizedRect BackRegion = new(970 / 2048d, 982 / 1152d, 92 / 2048d, 47 / 1152d);
    public static readonly NormalizedRect SuccessTitleRegion = new(130 / 2048d, 160 / 1152d, 260 / 2048d, 48 / 1152d);
    public static readonly NormalizedRect SuccessSkipRegion = new(1768 / 2048d, 1001 / 1152d, 54 / 2048d, 29 / 1152d);
    public static readonly NormalizedRect ListingDurationRegion = new(1180 / 2048d, 674 / 1152d, 160 / 2048d, 36 / 1152d);
    // Transition evidence only: these regions are not text templates from the photographed monitor.
    public static readonly NormalizedRect RewardTitleRegion = new(0.40, 0.18, 0.20, 0.14);
    public static readonly NormalizedRect ContinuePromptRegion = new(0.42, 0.91, 0.16, 0.085);
    private readonly TextTemplate _queue = Load("queue-full.png");
    private readonly TextTemplate _roll = Load("roll-failed.png");
    private readonly TextTemplate _back = Load("dialog-back.png");
    private readonly TextTemplate _backLowResolution = Load("dialog-back-720.png");
    private readonly TextTemplate _successTitle = Load("success-title.png");
    private readonly TextTemplate _successSkip = Load("success-skip.png");
    private readonly TextTemplate _listingDuration = Load("listing-duration-prefix.png");

    public PurchaseResult Detect(Bitmap toast)
    {
        if (MatchToast(toast, _queue, 914, 169, 222, 24))
        {
            return PurchaseResult.QueueFull;
        }

        return MatchToast(toast, _roll, 711, 169, 211, 24)
            ? PurchaseResult.RollFailed
            : PurchaseResult.Unknown;
    }

    public bool IsDialogBackVisible(Bitmap back) =>
        Match(back, _back, 2) || Match(back, _backLowResolution, 2);
    public bool IsSuccessTitleVisible(Bitmap title) => Match(title, _successTitle, 2);
    public bool IsSuccessSkipVisible(Bitmap skip) => Match(skip, _successSkip, 2);
    public bool IsListingDurationVisible(Bitmap row) =>
        MatchWithin(row, _listingDuration, new Size(160, 36));

    private static bool MatchWithin(Bitmap input, TextTemplate template, Size size)
    {
        using var scaled = new Bitmap(input, size);
        var ink = ExtractInk(scaled);
        var expanded = ExpandInk(ink, size, 1);
        var referenceExpanded = ExpandInk(template.Ink, template.Size, 1);
        var referenceCount = template.Ink.Count(value => value);
        if (referenceCount < 20) return false;

        // The centered label moves horizontally as the duration text changes width.
        // Match only the clock and "remaining" prefix; never interpret days as unlock seconds.
        for (var top = 0; top <= size.Height - template.Size.Height; top++)
        for (var left = 0; left <= size.Width - template.Size.Width; left++)
        {
            var actualCount = 0;
            var recall = 0;
            var precision = 0;
            for (var y = 0; y < template.Size.Height; y++)
            for (var x = 0; x < template.Size.Width; x++)
            {
                var sourceIndex = (top + y) * size.Width + left + x;
                var referenceIndex = y * template.Size.Width + x;
                if (template.Ink[referenceIndex] && expanded[sourceIndex]) recall++;
                if (!ink[sourceIndex]) continue;
                actualCount++;
                if (referenceExpanded[referenceIndex]) precision++;
            }
            if (actualCount >= referenceCount * 0.65 && actualCount <= referenceCount * 1.45 &&
                recall >= referenceCount * 0.90 && precision >= actualCount * 0.90)
                return true;
        }
        return false;
    }

    private static bool[] ExpandInk(bool[] ink, Size size, int radius)
    {
        var expanded = new bool[ink.Length];
        for (var y = 0; y < size.Height; y++)
        for (var x = 0; x < size.Width; x++)
        {
            if (!ink[y * size.Width + x]) continue;
            for (var yy = Math.Max(0, y - radius); yy <= Math.Min(size.Height - 1, y + radius); yy++)
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(size.Width - 1, x + radius); xx++)
                expanded[yy * size.Width + xx] = true;
        }
        return expanded;
    }

    private static bool MatchToast(Bitmap toast, TextTemplate template, int x, int y, int width, int height)
    {
        var region = new NormalizedRect(
            (x / 2048d - ToastRegion.X) / ToastRegion.Width,
            (y / 1152d - ToastRegion.Y) / ToastRegion.Height,
            width / 2048d / ToastRegion.Width,
            height / 1152d / ToastRegion.Height);
        using var crop = VisualAnalyzer.Crop(toast, region);
        return Match(crop, template);
    }

    private static TextTemplate Load(string name)
    {
        using var stream = typeof(ResultVisualDetector).Assembly.GetManifestResourceStream(
            $"DeltaMarketAssistant.Core.Assets.{name}") ??
            throw new InvalidOperationException($"缺少结果识别模板：{name}");
        using var bitmap = new Bitmap(stream);
        return new TextTemplate(bitmap.Size, ExtractInk(bitmap));
    }

    private static bool Match(Bitmap input, TextTemplate template, int tolerance = 1)
    {
        using var scaled = new Bitmap(template.Size.Width, template.Size.Height);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.DrawImage(input, new Rectangle(Point.Empty, scaled.Size),
                new Rectangle(Point.Empty, input.Size), GraphicsUnit.Pixel);
        }

        var ink = ExtractInk(scaled);
        var referenceCount = template.Ink.Count(value => value);
        var actualCount = ink.Count(value => value);
        if (referenceCount < 20 || actualCount < referenceCount * 0.65 || actualCount > referenceCount * 1.45)
        {
            return false;
        }

        // Symmetric foreground matching ignores dark backgrounds but rejects different text.
        var recall = NearbyMatches(template.Ink, ink, template.Size, tolerance) / (double)referenceCount;
        var precision = NearbyMatches(ink, template.Ink, template.Size, tolerance) / (double)actualCount;
        return recall >= 0.90 && precision >= 0.90;
    }

    private static bool[] ExtractInk(Bitmap bitmap)
    {
        var result = new bool[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            result[y * bitmap.Width + x] = pixel.R >= 100 && pixel.G >= 100 && pixel.B >= 100 &&
                Math.Abs(pixel.R - pixel.G) < 30 && Math.Abs(pixel.G - pixel.B) < 30;
        }
        return result;
    }

    private static int NearbyMatches(bool[] source, bool[] target, Size size, int tolerance)
    {
        var matches = 0;
        for (var y = 0; y < size.Height; y++)
        for (var x = 0; x < size.Width; x++)
        {
            if (!source[y * size.Width + x]) continue;
            var found = false;
            for (var dy = -tolerance; dy <= tolerance && !found; dy++)
            for (var dx = -tolerance; dx <= tolerance && !found; dx++)
            {
                var xx = x + dx;
                var yy = y + dy;
                found = xx >= 0 && xx < size.Width && yy >= 0 && yy < size.Height &&
                        target[yy * size.Width + xx];
            }
            if (found) matches++;
        }
        return matches;
    }

    private sealed record TextTemplate(Size Size, bool[] Ink);
}
