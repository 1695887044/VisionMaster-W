using System.Windows;

namespace G.Extensions.StoryBoard.EasingFunction;

public static class PointEx
{
    public static Point Add(this Point point, Point value)
    {
        return new Point(point.X + value.X, point.Y + value.Y);
    }

    public static Point Multiply(this Point point, double value)
    {
        return new Point(point.X * value, point.Y * value);
    }
}
