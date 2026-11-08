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

    public static DockRect Compact(DockRect area, DockEdge edge, double offset, double scale, bool expanded, bool minimized, int genres = 7)
    {
        double rail = (minimized ? 28 : 68) * scale;
        double length = Math.Min((minimized ? 112 : Math.Max(620, Math.Clamp(genres, 1, 16) * 66 + 158)) * scale, IsVertical(edge) ? area.Height : area.Width);
        double depth = minimized || !expanded ? rail : 508 * scale;
        double width = Math.Min(area.Width, IsVertical(edge) ? depth : length);
        double height = Math.Min(area.Height, IsVertical(edge) ? length : depth);
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, 1) : 0.5;
        return new(edge == DockEdge.Right ? area.Right - width : IsVertical(edge) ? area.X : area.X + (area.Width - width) * offset,
            edge == DockEdge.Bottom ? area.Bottom - height : !IsVertical(edge) ? area.Y : area.Y + (area.Height - height) * offset, width, height);
    }

    // Every category and file uses the same square module. Unused desktop remains transparent.
    public static DockRect DesktopRail(DockRect area, DockEdge edge, double scale, int stacks = 7, int tileSize = 88, double offset = 0.5)
    {
        var (side, slots, bands) = Grid(area, edge, scale, stacks + 1, tileSize);
        double length = Math.Min(stacks + 1, slots) * side;
        double depth = bands * side;
        double width = IsVertical(edge) ? depth : length;
        double height = IsVertical(edge) ? length : depth;
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, 1) : 0.5;
        return new(edge == DockEdge.Right ? area.Right - width : IsVertical(edge) ? area.X : area.X + (area.Width - width) * offset,
            edge == DockEdge.Bottom ? area.Bottom - height : !IsVertical(edge) ? area.Y : area.Y + (area.Height - height) * offset, width, height);
    }

    public static DockRect DesktopCell(DockRect area, DockEdge edge, double scale, int index, int stacks, int tileSize, double offset)
    {
        var root = DesktopRail(area, edge, scale, stacks, tileSize, offset);
        var (side, slots, bands) = Grid(area, edge, scale, stacks + 1, tileSize);
        index = Math.Clamp(index, 0, stacks);
        int band = index / slots, slot = index % slots;
        double x = IsVertical(edge) ? (edge == DockEdge.Right ? bands - 1 - band : band) * side : slot * side;
        double y = !IsVertical(edge) ? (edge == DockEdge.Bottom ? bands - 1 - band : band) * side : slot * side;
        return new(root.X + x, root.Y + y, side, side);
    }

    public static DockRect Flyout(DockRect area, DockEdge edge, int stackIndex, int stackCount, double scale, double width, double height, int tileSize = 88, double offset = 0.5)
    {
        var cell = DesktopCell(area, edge, scale, stackIndex, stackCount, tileSize, offset);
        var root = DesktopRail(area, edge, scale, stackCount, tileSize, offset);
        width = Math.Min(width * scale, Math.Max(1, area.Width - (IsVertical(edge) ? root.Width : 0)));
        height = Math.Min(height * scale, Math.Max(1, area.Height - (IsVertical(edge) ? 0 : root.Height)));
        double x = edge == DockEdge.Right ? root.X - width : edge == DockEdge.Left ? root.Right : cell.X;
        double y = edge == DockEdge.Bottom ? root.Y - height : edge == DockEdge.Top ? root.Bottom : cell.Y;
        return new(Math.Clamp(x, area.X, area.Right - width), Math.Clamp(y, area.Y, area.Bottom - height), width, height);
    }

    private static (double Side, int Slots, int Bands) Grid(DockRect area, DockEdge edge, double scale, int count, int tileSize)
    {
        double side = Math.Min(tileSize * scale, Math.Min(area.Width, area.Height));
        while (side > 1 && Math.Floor((IsVertical(edge) ? area.Height : area.Width) / side) * Math.Floor(Math.Max(0, (IsVertical(edge) ? area.Width : area.Height) - side * 2) / side) < count) side--;
        int slots = Math.Max(1, (int)((IsVertical(edge) ? area.Height : area.Width) / side));
        int bands = (int)Math.Ceiling(count / (double)slots);
        return (side, slots, bands);
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
