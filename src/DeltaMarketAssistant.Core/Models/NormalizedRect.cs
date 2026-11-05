using System.Drawing;

namespace DeltaMarketAssistant.Core.Models;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0 &&
                           X + Width <= 1.0001 && Y + Height <= 1.0001;

    public Rectangle ToPixels(Size clientSize)
    {
        var x = Math.Clamp((int)Math.Round(X * clientSize.Width), 0, Math.Max(0, clientSize.Width - 1));
        var y = Math.Clamp((int)Math.Round(Y * clientSize.Height), 0, Math.Max(0, clientSize.Height - 1));
        var right = Math.Clamp((int)Math.Round((X + Width) * clientSize.Width), x + 1, clientSize.Width);
        var bottom = Math.Clamp((int)Math.Round((Y + Height) * clientSize.Height), y + 1, clientSize.Height);
        return Rectangle.FromLTRB(x, y, right, bottom);
    }

    public Point CenterInPixels(Size clientSize)
    {
        var rect = ToPixels(clientSize);
        return new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    }

    public static NormalizedRect FromPixels(Rectangle rectangle, Size imageSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize));
        }

        return new NormalizedRect(
            rectangle.X / (double)imageSize.Width,
            rectangle.Y / (double)imageSize.Height,
            rectangle.Width / (double)imageSize.Width,
            rectangle.Height / (double)imageSize.Height);
    }
}
