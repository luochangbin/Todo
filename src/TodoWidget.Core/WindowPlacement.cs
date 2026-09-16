namespace TodoWidget.Core;

public sealed record WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    DockEdge DockEdge,
    double Anchor)
{
    public static WindowPlacement Default { get; } = new(100, 100, 330, 420, DockEdge.None, 0);
}
