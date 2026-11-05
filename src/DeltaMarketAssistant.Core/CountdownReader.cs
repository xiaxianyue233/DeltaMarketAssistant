using System.Drawing;
using System.Drawing.Text;

namespace DeltaMarketAssistant.Core;

public sealed record CountdownReading(int Seconds, string Text, double Confidence);
public sealed record CountdownGlyphTemplate(char Value, string Ink, double Aspect);

public sealed class CountdownReader
{
    private sealed record Glyph(char Value, bool[] Ink, double Aspect);
    private static readonly Lazy<Glyph[]> Templates = new(BuildTemplates);
    private const int Width = 20;
    private const int Height = 32;
    private readonly Glyph[] _templates;

    public CountdownReader(IEnumerable<CountdownGlyphTemplate>? custom = null)
    {
        var learned = new List<Glyph>();
        foreach (var template in custom ?? [])
        {
            try
            {
                var bytes = Convert.FromBase64String(template.Ink);
                if (char.IsAsciiDigit(template.Value) && bytes.Length == Width * Height &&
                    template.Aspect is > 0.1 and < 2)
                    learned.Add(new Glyph(template.Value, bytes.Select(b => b != 0).ToArray(), template.Aspect));
            }
            catch (FormatException) { }
        }
        _templates = Templates.Value.Concat(learned).ToArray();
    }

    public IReadOnlyList<CountdownGlyphTemplate> LearnSample(Bitmap line, int totalSeconds)
    {
        if (totalSeconds is < 0 or > 5999) return [];
        var parts = Split(line);
        var decoded = parts.Select(Recognize).ToArray();
        var minutes = (totalSeconds / 60).ToString();
        var seconds = (totalSeconds % 60).ToString();
        var minuteUnit = Array.FindIndex(decoded, g => g.Value == '分');
        var secondUnit = Array.FindIndex(decoded, g => g.Value == '秒');
        if (minuteUnit < minutes.Length || secondUnit - minuteUnit - 1 != seconds.Length) return [];
        var indices = Enumerable.Range(minuteUnit - minutes.Length, minutes.Length)
            .Concat(Enumerable.Range(minuteUnit + 1, seconds.Length)).ToArray();
        return indices.Zip(minutes + seconds, (index, value) =>
            new CountdownGlyphTemplate(value, Convert.ToBase64String(parts[index].Ink.Select(b => (byte)(b ? 1 : 0)).ToArray()),
                parts[index].Aspect)).ToArray();
    }

    public CountdownReading? Read(Bitmap line)
    {
        var parts = Split(line);
        if (parts.Count < 4 || parts.Count > 32) return null;
        var decoded = parts.Select(part => Recognize(part)).ToArray();
        var readings = new List<CountdownReading>();
        for (var start = 0; start < decoded.Length; start++)
        {
            // Only an explicitly matched lock icon may precede the first digit; never truncate an unknown digit.
            if (start > 1 || (start == 1 && decoded[0].Value != 'L')) continue;
            if (start > 0 && char.IsAsciiDigit(decoded[start - 1].Value)) continue;
            var i = start;
            var minutes = ReadDigits(decoded, ref i);
            if (minutes < 0 || i >= decoded.Length || decoded[i++].Value != '分') continue;
            var seconds = ReadDigits(decoded, ref i);
            if (seconds < 0 || seconds > 59 || i >= decoded.Length || decoded[i++].Value != '秒') continue;
            var confidence = decoded.Skip(start).Take(i - start).Min(g => g.Confidence);
            readings.Add(new CountdownReading(minutes * 60 + seconds,
                $"{minutes}分{seconds}秒", confidence));
        }
        return readings.Count == 1 ? readings[0] : null;
    }

    private static int ReadDigits((char Value, double Confidence)[] glyphs, ref int index)
    {
        var start = index;
        var value = 0;
        while (index < glyphs.Length && char.IsAsciiDigit(glyphs[index].Value))
        {
            value = value * 10 + glyphs[index++].Value - '0';
            if (index - start > 2) return -1;
        }
        return index == start ? -1 : value;
    }

    private (char Value, double Confidence) Recognize(Glyph input)
    {
        var scores = _templates.Select(template =>
        {
            var intersection = 0;
            var union = 0;
            for (var i = 0; i < input.Ink.Length; i++)
            {
                if (input.Ink[i] || template.Ink[i]) union++;
                if (input.Ink[i] && template.Ink[i]) intersection++;
            }
            var shape = union == 0 ? 0 : intersection / (double)union;
            var aspectPenalty = Math.Min(0.4, Math.Abs(Math.Log(input.Aspect / template.Aspect)) * 0.25);
            return (template.Value, Score: shape - aspectPenalty);
        }).GroupBy(g => g.Value).Select(g => g.MaxBy(v => v.Score))
            .OrderByDescending(g => g.Score).ToArray();
        return scores[0].Score >= 0.60 && scores[0].Score - scores[1].Score >= 0.055
            ? (scores[0].Value, scores[0].Score) : ('?', 0);
    }

    private static Glyph[] BuildTemplates()
    {
        var result = new List<Glyph>();
        foreach (var family in new[] { "Bahnschrift", "Segoe UI", "Arial", "Microsoft YaHei UI" })
        foreach (var style in new[] { FontStyle.Regular, FontStyle.Bold })
        foreach (var value in "0123456789分秒")
        {
            using var image = new Bitmap(70, 65);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font(family, 32, style, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.Black);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(value.ToString(), font, Brushes.White, 4, 4, StringFormat.GenericTypographic);
            }
            var glyphs = Split(image);
            if (glyphs.Count == 1) result.Add(glyphs[0] with { Value = value });
        }
        foreach (var name in typeof(CountdownReader).Assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".countdown-") && n.EndsWith(".png")))
        {
            var code = name.Split("countdown-")[1].Split('-')[0];
            if (!int.TryParse(code, out var value)) continue;
            using var stream = typeof(CountdownReader).Assembly.GetManifestResourceStream(name)!;
            using var image = new Bitmap(stream);
            var glyphs = Split(image);
            if (glyphs.Count == 1) result.Add(glyphs[0] with { Value = (char)value });
        }
        return result.ToArray();
    }

    private static List<Glyph> Split(Bitmap image)
    {
        var ink = new bool[image.Width, image.Height];
        var columns = new bool[image.Width];
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var p = image.GetPixel(x, y);
            ink[x, y] = Math.Min(p.R, Math.Min(p.G, p.B)) >= 110 &&
                        Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) < 55;
            columns[x] |= ink[x, y];
        }
        var result = new List<Glyph>();
        for (var x = 0; x < image.Width; x++)
        {
            if (!columns[x]) continue;
            var left = x;
            while (x + 1 < image.Width && columns[x + 1]) x++;
            var right = x;
            var top = image.Height;
            var bottom = -1;
            for (var xx = left; xx <= right; xx++)
            for (var y = 0; y < image.Height; y++)
                if (ink[xx, y]) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            var height = bottom - top + 1;
            var width = right - left + 1;
            if (height < 5 || width < 2) continue;
            var normalized = new bool[Width * Height];
            for (var yy = 0; yy < Height; yy++)
            for (var xx = 0; xx < Width; xx++)
                normalized[yy * Width + xx] = ink[left + Math.Min(width - 1, xx * width / Width),
                    top + Math.Min(height - 1, yy * height / Height)];
            result.Add(new Glyph('?', normalized, width / (double)height));
        }
        return result;
    }

    public string Describe(Bitmap image) =>
        string.Join(" ", Split(image).Select(g => { var r = Recognize(g); return $"{r.Value}:{r.Confidence:F2}"; }));
}
