namespace TodoWidget.Core;

public sealed record RectD(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Intersects(RectD other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}
