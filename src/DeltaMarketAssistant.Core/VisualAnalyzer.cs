using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public readonly record struct ImageStatistics(
    double MeanLuminance,
    double LuminanceDeviation,
    double GreenRatio,
    double EdgeRatio)
{
    public bool IsBlank => MeanLuminance < 4 || LuminanceDeviation < 1.5;
    public bool HasDetail => !IsBlank && LuminanceDeviation >= 7 && EdgeRatio >= 0.012;
}

public static class VisualAnalyzer
{
    private const int FingerprintWidth = 24;
    private const int FingerprintHeight = 12;

    public static unsafe ImageStatistics Analyze(Bitmap bitmap)
    {
        if (bitmap.Width == 0 || bitmap.Height == 0)
        {
            return default;
        }

        Bitmap? converted = null;
        var source = bitmap;
        if (bitmap.PixelFormat != PixelFormat.Format24bppRgb)
        {
            converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(converted);
            graphics.DrawImageUnscaled(bitmap, 0, 0);
            source = converted;
        }

        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var step = Math.Max(1, (int)Math.Sqrt(source.Width * source.Height / 14000d));
            long count = 0;
            double sum = 0;
            double sumSquares = 0;
            long green = 0;
            long edges = 0;

            for (var y = 0; y < source.Height; y += step)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                double? previousLuminance = null;
                for (var x = 0; x < source.Width; x += step)
                {
                    var pixel = row + x * 3;
                    var blue = pixel[0];
                    var greenChannel = pixel[1];
                    var red = pixel[2];
                    var luminance = 0.2126 * red + 0.7152 * greenChannel + 0.0722 * blue;

                    sum += luminance;
                    sumSquares += luminance * luminance;
                    count++;

                    if (greenChannel >= 80 && greenChannel >= red + 14 && greenChannel + 8 >= blue)
                    {
                        green++;
                    }

                    if (previousLuminance.HasValue && Math.Abs(luminance - previousLuminance.Value) >= 18)
                    {
                        edges++;
                    }

                    previousLuminance = luminance;
                }
            }

            if (count == 0)
            {
                return default;
            }

            var mean = sum / count;
            var variance = Math.Max(0, sumSquares / count - mean * mean);
            return new ImageStatistics(
                mean,
                Math.Sqrt(variance),
                green / (double)count,
                edges / (double)count);
        }
        finally
        {
            source.UnlockBits(data);
            converted?.Dispose();
        }
    }

    public static string CreateFingerprint(Bitmap source) =>
        Convert.ToBase64String(CreateFingerprintBytes(source));

    public static string CreateFingerprint(Bitmap source, NormalizedRect region)
    {
        using var cropped = Crop(source, region);
        return CreateFingerprint(cropped);
    }

    public static double FingerprintDifference(Bitmap current, string referenceBase64)
    {
        if (string.IsNullOrWhiteSpace(referenceBase64))
        {
            return 1;
        }

        try
        {
            return FingerprintDifference(CreateFingerprintBytes(current), Convert.FromBase64String(referenceBase64));
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    public static double FingerprintDifference(string firstBase64, string secondBase64)
    {
        try
        {
            return FingerprintDifference(Convert.FromBase64String(firstBase64), Convert.FromBase64String(secondBase64));
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    public static Bitmap Crop(Bitmap source, NormalizedRect region)
    {
        var pixels = region.ToPixels(source.Size);
        var result = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(source, new Rectangle(Point.Empty, result.Size), pixels, GraphicsUnit.Pixel);
        return result;
    }

    private static byte[] CreateFingerprintBytes(Bitmap source)
    {
        using var sample = new Bitmap(FingerprintWidth, FingerprintHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, FingerprintWidth, FingerprintHeight));
        }

        var result = new byte[FingerprintWidth * FingerprintHeight * 3];
        var data = sample.LockBits(
            new Rectangle(0, 0, sample.Width, sample.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < FingerprintHeight; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, result, y * FingerprintWidth * 3, FingerprintWidth * 3);
            }
        }
        finally
        {
            sample.UnlockBits(data);
        }

        return result;
    }

    private static double FingerprintDifference(byte[] first, byte[] second)
    {
        if (first.Length == 0 || first.Length != second.Length)
        {
            return 1;
        }

        long difference = 0;
        for (var index = 0; index < first.Length; index++)
        {
            difference += Math.Abs(first[index] - second[index]);
        }

        return difference / (first.Length * 255d);
    }
}
