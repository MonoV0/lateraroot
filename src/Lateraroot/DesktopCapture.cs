using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lateraroot;

/// <summary>DXGI GPU desktop duplication. Only requested widget rectangles are copied to CPU memory.</summary>
internal sealed unsafe class DesktopCapture : IDisposable
{
    internal static long RegionCopies;
    internal readonly record struct Area(int X, int Y, int Width, int Height)
    {
        internal bool Valid => Width > 0 && Height > 0 && (long)Width * Height <= 4_000_000;
    }
    internal sealed class Pixels(Area area, byte[] bytes) { internal Area Area = area; internal byte[] Bytes = bytes; }
    private sealed class Slot { internal IntPtr Texture; internal Area Area; internal byte[] Current = [], Previous = []; internal bool Published; }
    private readonly Dictionary<int, Slot> slots = [];
    private IntPtr device, context, duplication;
    private NativeRect[] dirty = [];
    private MoveRect[] moves = [];
    private int dirtyCount, moveCount;
    private bool knownDamage;
    internal Area Bounds { get; private set; }
    internal int Rotation { get; private set; }
    private static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static IntPtr Query(IntPtr source, string iid) { Guid id = new(iid); Check(Marshal.QueryInterface(source, in id, out var result)); return result; }
    private static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }

    internal DesktopCapture(IntPtr monitor)
    {
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero, output = IntPtr.Zero, output1 = IntPtr.Zero;
        try
        {
            Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
            Check(CreateDXGIFactory1(ref iid, out factory));
            for (uint a = 0; ; a++)
            {
                int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(factory, 12))(factory, a, &adapter);
                if (hr == unchecked((int)0x887A0002)) break;
                Check(hr);
                for (uint o = 0; ; o++)
                {
                    hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(adapter, 7))(adapter, o, &output);
                    if (hr == unchecked((int)0x887A0002)) break;
                    Check(hr); OutputDescription desc;
                    Check(((delegate* unmanaged[Stdcall]<nint, OutputDescription*, int>)Method(output, 7))(output, &desc));
                    if (desc.Monitor == monitor)
                    {
                        Rotation = (int)desc.Rotation;
                        if (Rotation is not (0 or 1)) throw new NotSupportedException("Rotated capture falls back to the static material in this preview.");
                        Bounds = new(desc.Left, desc.Top, desc.Right - desc.Left, desc.Bottom - desc.Top);
                        Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out _, out context));
                        output1 = Query(output, "00cddea8-939b-4b83-a340-a685226666cc");
                        IntPtr result;
                        Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Method(output1, 22))(output1, device, &result));
                        duplication = result; return;
                    }
                    Release(ref output);
                }
                Release(ref output); Release(ref adapter);
            }
            throw new NotSupportedException("No capture output found for this monitor.");
        }
        catch { Dispose(); throw; }
        finally { Release(ref output1); Release(ref output); Release(ref adapter); Release(ref factory); }
    }

    internal List<(int Id, Pixels Frame)> Read(IReadOnlyList<(int Id, Area Rect)> requests, IReadOnlySet<int>? force = null)
    {
        var result = new List<(int, Pixels)>();
        foreach (int id in new List<int>(slots.Keys))
            if (!System.Linq.Enumerable.Any(requests, r => r.Id == id)) { var old = slots[id]; Release(ref old.Texture); slots.Remove(id); }
        IntPtr resource = IntPtr.Zero, texture = IntPtr.Zero;
        FrameInfo info;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, FrameInfo*, nint*, int>)Method(duplication, 8))(duplication, 100, &info, &resource);
        if (hr == unchecked((int)0x887A0027)) return result; // DXGI_ERROR_WAIT_TIMEOUT
        Check(hr);
        try
        {
            texture = Query(resource, "6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            ReadDamage(info.Metadata);
            foreach (var (id, requested) in requests)
            {
                int left = Math.Max(requested.X, Bounds.X), top = Math.Max(requested.Y, Bounds.Y);
                int right = Math.Min(requested.X + requested.Width, Bounds.X + Bounds.Width), bottom = Math.Min(requested.Y + requested.Height, Bounds.Y + Bounds.Height);
                var area = new Area(left, top, right - left, bottom - top);
                if (!area.Valid) continue;
                if (!slots.TryGetValue(id, out var slot)) slots[id] = slot = new();
                bool moved = slot.Area != area;
                bool refresh = force?.Contains(id) == true;
                if (!refresh && slot.Published && !moved && (info.Presented == 0 || !Touches(area))) continue;
                if (slot.Texture == IntPtr.Zero || slot.Area.Width != area.Width || slot.Area.Height != area.Height)
                {
                    Release(ref slot.Texture);
                    var description = new TextureDescription { Width = (uint)area.Width, Height = (uint)area.Height, MipLevels = 1, ArraySize = 1, Format = 87, Samples = 1, Usage = 3, CpuAccess = 0x20000 };
                    IntPtr staging;
                    Check(((delegate* unmanaged[Stdcall]<nint, TextureDescription*, nint, nint*, int>)Method(device, 5))(device, &description, 0, &staging));
                    slot.Texture = staging; slot.Current = new byte[area.Width * area.Height * 4]; slot.Previous = new byte[slot.Current.Length]; slot.Published = false;
                }
                slot.Area = area;
                var box = new Box { Left = (uint)(area.X - Bounds.X), Top = (uint)(area.Y - Bounds.Y), Right = (uint)(area.X - Bounds.X + area.Width), Bottom = (uint)(area.Y - Bounds.Y + area.Height), Back = 1 };
                ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, Box*, void>)Method(context, 46))(context, slot.Texture, 0, 0, 0, 0, texture, 0, &box);
                Mapped mapped;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, Mapped*, int>)Method(context, 14))(context, slot.Texture, 0, 1, 0, &mapped));
                try
                {
                    int stride = area.Width * 4;
                    for (int y = 0; y < area.Height; y++) Marshal.Copy(mapped.Data + (int)(y * mapped.RowPitch), slot.Current, y * stride, stride);
                    // DXGI desktop alpha is not meaningful. Normalize it before
                    // comparing frames so unused bits cannot trigger redraws.
                    var words = MemoryMarshal.Cast<byte, uint>(slot.Current.AsSpan());
                    for (int i = 0; i < words.Length; i++) words[i] |= 0xFF000000;
                    RegionCopies++;
                }
                finally { ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Method(context, 15))(context, slot.Texture, 0); }
                if (!refresh && slot.Published && !moved && slot.Current.AsSpan().SequenceEqual(slot.Previous)) continue;
                (slot.Current, slot.Previous) = (slot.Previous, slot.Current); slot.Published = true;
                result.Add((id, new(area, slot.Previous)));
            }
            // BitmapSource.Create copies the returned buffer before the next Read.
            return result;
        }
        finally
        {
            Release(ref texture); Release(ref resource);
            ((delegate* unmanaged[Stdcall]<nint, int>)Method(duplication, 14))(duplication);
        }
    }
    private void ReadDamage(uint metadata)
    {
        knownDamage = false; dirtyCount = moveCount = 0;
        if (metadata == 0 || metadata > 1_048_576) return;
        int rects = (int)metadata / sizeof(NativeRect) + 1, moveRects = (int)metadata / sizeof(MoveRect) + 1;
        if (dirty.Length < rects) dirty = new NativeRect[rects];
        if (moves.Length < moveRects) moves = new MoveRect[moveRects];
        uint dirtyBytes, moveBytes;
        fixed (NativeRect* d = dirty)
        fixed (MoveRect* m = moves)
        {
            int a = ((delegate* unmanaged[Stdcall]<nint, uint, NativeRect*, uint*, int>)Method(duplication, 9))(duplication, (uint)(dirty.Length * sizeof(NativeRect)), d, &dirtyBytes);
            int b = ((delegate* unmanaged[Stdcall]<nint, uint, MoveRect*, uint*, int>)Method(duplication, 10))(duplication, (uint)(moves.Length * sizeof(MoveRect)), m, &moveBytes);
            if (a < 0 || b < 0) return;
        }
        dirtyCount = (int)dirtyBytes / sizeof(NativeRect); moveCount = (int)moveBytes / sizeof(MoveRect); knownDamage = true;
    }
    private bool Touches(Area area)
    {
        if (!knownDamage) return true;
        int x = area.X - Bounds.X, y = area.Y - Bounds.Y;
        bool Intersects(NativeRect r) => r.Left < x + area.Width && r.Right > x && r.Top < y + area.Height && r.Bottom > y;
        for (int i = 0; i < dirtyCount; i++) if (Intersects(dirty[i])) return true;
        for (int i = 0; i < moveCount; i++)
        {
            var move = moves[i];
            if (Intersects(move.Destination) || Intersects(new NativeRect { Left = move.X, Top = move.Y, Right = move.X + move.Destination.Right - move.Destination.Left, Bottom = move.Y + move.Destination.Bottom - move.Destination.Top })) return true;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (var slot in slots.Values) Release(ref slot.Texture);
        slots.Clear(); Release(ref duplication); Release(ref context); Release(ref device);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OutputDescription { public fixed char Name[32]; public int Left, Top, Right, Bottom, Attached; public uint Rotation; public IntPtr Monitor; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameInfo { public long Presented, MouseUpdated; public uint Accumulated; public int Coalesced, Protected, PointerX, PointerY, PointerVisible; public uint Metadata, PointerShape; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription { public uint Width, Height, MipLevels, ArraySize, Format, Samples, Quality, Usage, Bind, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MoveRect { public int X, Y; public NativeRect Destination; }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out uint level, out IntPtr context);
}
