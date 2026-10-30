// webForCore: GDI+ regions on SkiaSharp path operations (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    // A region is infinite, or the area of a path (combined with Skia's path operations). GDI+'s infinite region is the
    // rectangle (-4194304, -4194304, 8388608, 8388608).
    internal sealed class GpRegion : IDisposable
    {
        public static readonly RectangleF InfiniteRect = new RectangleF(-4194304, -4194304, 8388608, 8388608);
        public bool Infinite = true;
        public SKPath Path = new SKPath();

        public GpRegion Clone() => new GpRegion { Infinite = Infinite, Path = new SKPath(Path) };
        public void Dispose() => Path?.Dispose();

        // GDI+ normalizes a rectangle with a negative width or height.
        public static GpRegion FromRect(RectangleF r)
        {
            if (r.Width < 0) { r.X += r.Width; r.Width = -r.Width; }
            if (r.Height < 0) { r.Y += r.Height; r.Height = -r.Height; }
            var region = new GpRegion { Infinite = false };
            if (r.Width > 0 && r.Height > 0) region.Path.AddRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom));
            return region;
        }

        public static GpRegion FromPath(SKPath path) => new GpRegion { Infinite = false, Path = new SKPath(path) };

        public SKPath AsPath()
        {
            if (!Infinite) return new SKPath(Path);
            var p = new SKPath();
            p.AddRect(new SKRect(InfiniteRect.Left, InfiniteRect.Top, InfiniteRect.Right, InfiniteRect.Bottom));
            return p;
        }

        public bool IsEmpty => !Infinite && Path.IsEmpty || !Infinite && Path.Bounds.IsEmpty;

        public void SetEmpty()
        {
            Infinite = false;
            Path.Reset();
        }

        public void SetInfinite()
        {
            Infinite = true;
            Path.Reset();
        }

        public void Combine(GpRegion other, CombineMode mode)
        {
            if (mode == CombineMode.Replace)
            {
                Infinite = other.Infinite;
                Path.Dispose();
                Path = new SKPath(other.Path);
                return;
            }
            if (mode == CombineMode.Intersect && Infinite) { Combine(other, CombineMode.Replace); return; }
            // An empty operand leaves an infinite region infinite (Union, Xor, Exclude).
            if (Infinite && other.IsEmpty && mode != CombineMode.Complement) return;
            if (mode == CombineMode.Intersect && other.Infinite) return;
            if (mode == CombineMode.Union && (Infinite || other.Infinite)) { SetInfinite(); return; }
            using var a = AsPath();
            using var b = other.AsPath();
            var op = mode switch
            {
                CombineMode.Intersect => SKPathOp.Intersect,
                CombineMode.Union => SKPathOp.Union,
                CombineMode.Xor => SKPathOp.Xor,
                CombineMode.Exclude => SKPathOp.Difference,
                _ => SKPathOp.ReverseDifference, // Complement: other - this
            };
            var result = a.Op(b, op) ?? new SKPath();
            Path.Dispose();
            Path = result;
            Infinite = false;
        }

        public void Transform(SKMatrix m)
        {
            if (Infinite) return;
            Path.Transform(m);
        }

        public RectangleF Bounds
        {
            get
            {
                if (Infinite) return InfiniteRect;
                var b = Path.TightBounds;
                return b.IsEmpty ? RectangleF.Empty : new RectangleF(b.Left, b.Top, b.Width, b.Height);
            }
        }

        // Half-open, as GDI+: a rectangle contains its left and top edges and not its right and bottom ones.
        public bool Contains(float x, float y) => Infinite || Path.Contains(x + 1f / 1024, y + 1f / 1024);

        public bool Intersects(RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            if (Infinite) return true;
            using var rect = new SKPath();
            rect.AddRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom));
            using var i = Path.Op(rect, SKPathOp.Intersect);
            return i != null && !i.IsEmpty && !i.Bounds.IsEmpty;
        }

        public bool EqualTo(GpRegion other)
        {
            if (Infinite || other.Infinite) return Infinite == other.Infinite;
            using var x = Path.Op(other.Path, SKPathOp.Xor);
            return x == null || x.IsEmpty || x.TightBounds.Width * x.TightBounds.Height < 1e-6f;
        }

        // Pixel-aligned rectangles covering the region (GetRegionScans).
        public List<RectangleF> Scans()
        {
            var result = new List<RectangleF>();
            if (Infinite) { result.Add(InfiniteRect); return result; }
            using var region = new SKRegion();
            var b = Path.Bounds;
            region.SetPath(Path, new SKRegion(new SKRectI((int)Math.Floor(b.Left), (int)Math.Floor(b.Top), (int)Math.Ceiling(b.Right), (int)Math.Ceiling(b.Bottom))));
            using var iterator = region.CreateRectIterator();
            while (iterator.Next(out var r)) result.Add(new RectangleF(r.Left, r.Top, r.Width, r.Height));
            return result;
        }
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        static GpRegion Region(HandleRef r) => Get<GpRegion>(r);

        internal static int GdipCreateRegion(out IntPtr region) { region = NewHandle(new GpRegion()); return Ok; }
        internal static int GdipCreateRegionRect(ref RectangleF gprectf, out IntPtr region) { region = NewHandle(GpRegion.FromRect(gprectf)); return Ok; }
        internal static int GdipCreateRegionRectI(ref Rectangle gprect, out IntPtr region) { region = NewHandle(GpRegion.FromRect(gprect)); return Ok; }

        internal static int GdipCreateRegionPath(HandleRef path, out IntPtr region)
        {
            region = IntPtr.Zero;
            var p = Get<GpPath>(path);
            if (p == null) return InvalidParameter;
            using var sk = p.ToSkia();
            region = NewHandle(GpRegion.FromPath(sk));
            return Ok;
        }

        internal static int GdipCloneRegion(HandleRef region, out IntPtr cloneregion) { cloneregion = IntPtr.Zero; var r = Region(region); if (r == null) return InvalidParameter; cloneregion = NewHandle(r.Clone()); return Ok; }
        internal static int GdipDeleteRegion(HandleRef region) => Free(region);
        internal static int GdipSetInfinite(HandleRef region) { var r = Region(region); if (r == null) return InvalidParameter; r.SetInfinite(); return Ok; }
        internal static int GdipSetEmpty(HandleRef region) { var r = Region(region); if (r == null) return InvalidParameter; r.SetEmpty(); return Ok; }

        static int Combine(HandleRef region, GpRegion other, CombineMode mode)
        {
            var r = Region(region);
            if (r == null || other == null || mode < CombineMode.Replace || mode > CombineMode.Complement) return InvalidParameter;
            r.Combine(other, mode);
            return Ok;
        }

        internal static int GdipCombineRegionRect(HandleRef region, ref RectangleF gprectf, CombineMode mode) => Combine(region, GpRegion.FromRect(gprectf), mode);
        internal static int GdipCombineRegionRectI(HandleRef region, ref Rectangle gprect, CombineMode mode) => Combine(region, GpRegion.FromRect(gprect), mode);

        internal static int GdipCombineRegionPath(HandleRef region, HandleRef path, CombineMode mode)
        {
            var p = Get<GpPath>(path);
            if (p == null) return InvalidParameter;
            using var sk = p.ToSkia();
            return Combine(region, GpRegion.FromPath(sk), mode);
        }

        internal static int GdipCombineRegionRegion(HandleRef region, HandleRef region2, CombineMode mode) => Combine(region, Region(region2)?.Clone(), mode);
        internal static int GdipTranslateRegion(HandleRef region, float dx, float dy) { var r = Region(region); if (r == null) return InvalidParameter; r.Transform(SKMatrix.CreateTranslation(dx, dy)); return Ok; }
        internal static int GdipTranslateRegionI(HandleRef region, int dx, int dy) => GdipTranslateRegion(region, dx, dy);

        internal static int GdipTransformRegion(HandleRef region, HandleRef matrix)
        {
            var r = Region(region);
            var m = Get<GpMatrix>(matrix);
            if (r == null || m == null) return InvalidParameter;
            r.Transform(m.ToSkia());
            return Ok;
        }

        internal static int GdipGetRegionBounds(HandleRef region, HandleRef graphics, out RectangleF gprectf)
        {
            var r = Region(region);
            gprectf = r?.Bounds ?? default;
            return r == null || Get<GpGraphics>(graphics) == null ? InvalidParameter : Ok;
        }

        internal static int GdipIsEmptyRegion(HandleRef region, HandleRef graphics, out int boolean) { var r = Region(region); boolean = r?.IsEmpty == true ? 1 : 0; return r == null || Get<GpGraphics>(graphics) == null ? InvalidParameter : Ok; }
        internal static int GdipIsInfiniteRegion(HandleRef region, HandleRef graphics, out int boolean) { var r = Region(region); boolean = r?.Infinite == true ? 1 : 0; return r == null || Get<GpGraphics>(graphics) == null ? InvalidParameter : Ok; }

        internal static int GdipIsEqualRegion(HandleRef region, HandleRef region2, HandleRef graphics, out int boolean)
        {
            var a = Region(region);
            var b = Region(region2);
            boolean = a != null && b != null && a.EqualTo(b) ? 1 : 0;
            return a == null || b == null || Get<GpGraphics>(graphics) == null ? InvalidParameter : Ok;
        }

        internal static int GdipIsVisibleRegionPoint(HandleRef region, float X, float Y, HandleRef graphics, out int boolean) { var r = Region(region); boolean = r?.Contains(X, Y) == true ? 1 : 0; return r == null ? InvalidParameter : Ok; }
        internal static int GdipIsVisibleRegionPointI(HandleRef region, int X, int Y, HandleRef graphics, out int boolean) => GdipIsVisibleRegionPoint(region, X, Y, graphics, out boolean);
        internal static int GdipIsVisibleRegionRect(HandleRef region, float X, float Y, float width, float height, HandleRef graphics, out int boolean) { var r = Region(region); boolean = r?.Intersects(new RectangleF(X, Y, width, height)) == true ? 1 : 0; return r == null ? InvalidParameter : Ok; }
        internal static int GdipIsVisibleRegionRectI(HandleRef region, int X, int Y, int width, int height, HandleRef graphics, out int boolean) => GdipIsVisibleRegionRect(region, X, Y, width, height, graphics, out boolean);

        internal static int GdipGetRegionScansCount(HandleRef region, out int count, HandleRef matrix)
        {
            count = 0;
            var r = Region(region);
            var m = Get<GpMatrix>(matrix);
            if (r == null || m == null) return InvalidParameter;
            var copy = r.Clone();
            copy.Transform(m.ToSkia());
            count = copy.Scans().Count;
            copy.Dispose();
            return Ok;
        }

        internal static int GdipGetRegionScans(HandleRef region, RectangleF* rects, out int count, HandleRef matrix)
        {
            count = 0;
            var r = Region(region);
            var m = Get<GpMatrix>(matrix);
            if (r == null || m == null) return InvalidParameter;
            var copy = r.Clone();
            copy.Transform(m.ToSkia());
            var scans = copy.Scans();
            copy.Dispose();
            for (int i = 0; i < scans.Count; i++) rects[i] = scans[i];
            count = scans.Count;
            return Ok;
        }

        // An HRGN is a region handle, as with libgdiplus (System.Drawing's Unix ReleaseHrgn deletes it as a region).
        // An infinite region has no HRGN (GDI+ returns null).
        internal static int GdipGetRegionHRgn(HandleRef region, HandleRef graphics, out IntPtr hrgn)
        {
            hrgn = IntPtr.Zero;
            var r = Region(region);
            if (r == null) return InvalidParameter;
            if (!r.Infinite) hrgn = NewHandle(r.Clone());
            return Ok;
        }

        internal static int GdipCreateRegionHrgn(IntPtr hRgn, out IntPtr region)
        {
            region = IntPtr.Zero;
            var r = hRgn == IntPtr.Zero ? null : Get<GpRegion>(new HandleRef(null, hRgn));
            if (r == null) return InvalidParameter;
            region = NewHandle(r.Clone());
            return Ok;
        }

        // Region data, in GDI+'s format: size (of what follows), checksum (CRC-32 of what follows it), version
        // 0xDBC01002, combine-node count, then the node tree. Nodes: 1-5 Intersect/Union/Xor/Exclude/Complement
        // (two children follow), 0x10000000 rect, 0x10000001 path (size, then a path record), 0x10000002 empty,
        // 0x10000003 infinite. A region is written as one node; any tree is read.
        static byte[] RegionData(GpRegion r)
        {
            var body = new List<byte>();
            void U(uint v) => body.AddRange(BitConverter.GetBytes(v));
            void F(float v) => body.AddRange(BitConverter.GetBytes(v));
            U(0xDBC01002); U(0);
            if (r.Infinite) U(0x10000003);
            else if (r.IsEmpty) U(0x10000002);
            else if (r.Path.IsRect)
            {
                var b = r.Path.Bounds;
                U(0x10000000); F(b.Left); F(b.Top); F(b.Width); F(b.Height);
            }
            else
            {
                var gp = GpPath.FromSkia(r.Path, r.Path.FillType == SKPathFillType.Winding ? FillMode.Winding : FillMode.Alternate);
                int n = gp.Count, pad = (4 - n % 4) % 4;
                U(0x10000001); U((uint)(12 + n * 8 + n + pad));
                U(0xDBC01001); U((uint)n); U(0);
                foreach (var p in gp.Points) { F(p.X); F(p.Y); }
                body.AddRange(gp.Types);
                for (int i = 0; i < pad; i++) body.Add(0);
            }
            var result = new List<byte>();
            result.AddRange(BitConverter.GetBytes((uint)(body.Count + 4)));
            result.AddRange(BitConverter.GetBytes(Crc32(body)));
            result.AddRange(body);
            return result.ToArray();
        }

        static uint Crc32(List<byte> data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
            }
            return ~crc;
        }

        static float ReadFloat(byte[] d, ref int at) { float v = BitConverter.ToSingle(d, at); at += 4; return v; }

        static GpRegion ReadRegionNode(byte[] d, ref int at)
        {
            uint type = BitConverter.ToUInt32(d, at); at += 4;
            switch (type)
            {
                case 0x10000000:
                    return GpRegion.FromRect(new RectangleF(ReadFloat(d, ref at), ReadFloat(d, ref at), ReadFloat(d, ref at), ReadFloat(d, ref at)));
                case 0x10000001:
                {
                    int size = BitConverter.ToInt32(d, at); at += 4;
                    int start = at;
                    if (BitConverter.ToUInt32(d, at) != 0xDBC01001) throw new FormatException();
                    int n = BitConverter.ToInt32(d, at + 4), flags = BitConverter.ToInt32(d, at + 8);
                    at += 12;
                    if (n <= 0) throw new FormatException();
                    var path = new GpPath { FillMode = FillMode.Winding };
                    bool compressed = (flags & 0x4000) != 0;
                    for (int i = 0; i < n; i++)
                    {
                        PointF p;
                        if (compressed) { p = new PointF(BitConverter.ToInt16(d, at), BitConverter.ToInt16(d, at + 2)); at += 4; }
                        else p = new PointF(ReadFloat(d, ref at), ReadFloat(d, ref at));
                        path.Points.Add(p);
                    }
                    for (int i = 0; i < n; i++) path.Types.Add(d[at + i]);
                    at = start + size;
                    using var sk = path.ToSkia();
                    return GpRegion.FromPath(sk);
                }
                case 0x10000002: { var r = new GpRegion(); r.SetEmpty(); return r; }
                case 0x10000003: return new GpRegion();
                case >= 1 and <= 5:
                {
                    var left = ReadRegionNode(d, ref at);
                    var right = ReadRegionNode(d, ref at);
                    left.Combine(right, (CombineMode)type);
                    return left;
                }
                default: throw new FormatException();
            }
        }

        internal static int GdipGetRegionDataSize(HandleRef region, out int bufferSize)
        {
            var r = Region(region);
            bufferSize = r == null ? 0 : RegionData(r).Length;
            return r == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetRegionData(HandleRef region, byte[] regionData, int bufferSize, out int sizeFilled)
        {
            sizeFilled = 0;
            var r = Region(region);
            if (r == null || regionData == null) return InvalidParameter;
            var data = RegionData(r);
            if (bufferSize < data.Length || regionData.Length < data.Length) return InsufficientBuffer;
            Array.Copy(data, regionData, data.Length);
            sizeFilled = data.Length;
            return Ok;
        }

        internal static int GdipCreateRegionRgnData(byte[] rgndata, int size, out IntPtr region)
        {
            region = IntPtr.Zero;
            if (rgndata == null) return InvalidParameter;
            if (size < 20 || rgndata.Length < size) return GenericError;
            try
            {
                if (BitConverter.ToUInt32(rgndata, 8) != 0xDBC01002) return GenericError;
                int at = 16;
                region = NewHandle(ReadRegionNode(rgndata, ref at));
                return Ok;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is IndexOutOfRangeException)
            {
                return GenericError;
            }
        }
    }
    }
}
