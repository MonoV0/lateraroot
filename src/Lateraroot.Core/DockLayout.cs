namespace Lateraroot.Core;

public readonly record struct DockRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Shared physical-pixel geometry for all four edges; independent of WPF and monitor origins.</summary>
public static class DockLayout
{
    public static bool IsVertical(DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;

    public static DockRect Compact(DockRect area, DockEdge edge, double offset, double scale, bool expanded, bool minimized)
    {
        double rail = (minimized ? 28 : 68) * scale;
        double length = Math.Min((minimized ? 112 : 620) * scale, IsVertical(edge) ? area.Height : area.Width);
        double depth = minimized || !expanded ? rail : 508 * scale;
        double width = Math.Min(area.Width, IsVertical(edge) ? depth : length);
        double height = Math.Min(area.Height, IsVertical(edge) ? length : depth);
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, 1) : 0.5;
        return new(edge == DockEdge.Right ? area.Right - width : IsVertical(edge) ? area.X : area.X + (area.Width - width) * offset,
            edge == DockEdge.Bottom ? area.Bottom - height : !IsVertical(edge) ? area.Y : area.Y + (area.Height - height) * offset, width, height);
    }

    public static DockRect DesktopRail(DockRect area, DockEdge edge, double scale)
    {
        double rail = 72 * scale;
        return new(edge == DockEdge.Right ? area.Right - rail : area.X, edge == DockEdge.Bottom ? area.Bottom - rail : area.Y,
            IsVertical(edge) ? rail : area.Width, IsVertical(edge) ? area.Height : rail);
    }

    public static DockRect Flyout(DockRect area, DockEdge edge, int stackIndex, int stackCount, double scale, double width, double height)
    {
        double rail = 72 * scale;
        width = Math.Min(width * scale, Math.Max(1, area.Width - (IsVertical(edge) ? rail : 0)));
        height = Math.Min(height * scale, Math.Max(1, area.Height - (IsVertical(edge) ? 0 : rail)));
        double fraction = Math.Clamp(stackIndex / (double)Math.Max(1, stackCount), 0, 1);
        double x = edge == DockEdge.Right ? area.Right - rail - width : edge == DockEdge.Left ? area.X + rail : area.X + (area.Width - 48 * scale) * fraction;
        double y = edge == DockEdge.Bottom ? area.Bottom - rail - height : edge == DockEdge.Top ? area.Y + rail : area.Y + (area.Height - 48 * scale) * fraction;
        return new(Math.Clamp(x, area.X, area.Right - width), Math.Clamp(y, area.Y, area.Bottom - height), width, height);
    }

    public static (DockEdge Edge, double Offset) Snap(DockRect area, double pointerX, double pointerY, double scale, double lengthDip = 620)
    {
        var edge = new[] { (DockEdge.Left, Math.Abs(pointerX - area.X)), (DockEdge.Right, Math.Abs(pointerX - area.Right)),
            (DockEdge.Top, Math.Abs(pointerY - area.Y)), (DockEdge.Bottom, Math.Abs(pointerY - area.Bottom)) }.MinBy(x => x.Item2).Item1;
        double length = Math.Min(lengthDip * scale, IsVertical(edge) ? area.Height : area.Width);
        double available = (IsVertical(edge) ? area.Height : area.Width) - length;
        double offset = available <= 0 ? 0 : ((IsVertical(edge) ? pointerY - area.Y : pointerX - area.X) - length / 2) / available;
        return (edge, Math.Clamp(offset, 0, 1));
    }
}
