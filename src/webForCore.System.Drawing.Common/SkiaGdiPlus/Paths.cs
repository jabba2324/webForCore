// webForCore: the GDI+ flat API for paths and path iterators (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    internal sealed class GpPathIterator
    {
        public GpPath Path;
        public int Subpath, TypeIndex, Marker;
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        static GpPath Path(IntPtr path) => Get<GpPath>(path);
        static int PathDo(IntPtr path, Action<GpPath> action) { var p = Path(path); if (p == null) return InvalidParameter; action(p); return Ok; }
        static PointF[] ToF(Point[] points, int count) => points.Take(count).Select(p => (PointF)p).ToArray();

        internal static int GdipCreatePath(FillMode brushMode, out IntPtr path) { path = NewHandle(new GpPath { FillMode = brushMode }); return Ok; }

        internal static int GdipCreatePath2(PointF[] points, byte[] types, int count, FillMode brushMode, out IntPtr path)
        {
            path = IntPtr.Zero;
            if (points == null || types == null || count < 0) return InvalidParameter;
            var p = new GpPath { FillMode = brushMode };
            p.Points.AddRange(points.Take(count));
            p.Types.AddRange(types.Take(count));
            p.StartNew = count == 0 || (types[count - 1] & (byte)PathPointType.CloseSubpath) != 0;
            path = NewHandle(p);
            return Ok;
        }

        internal static int GdipCreatePath2I(Point[] points, byte[] types, int count, FillMode brushMode, out IntPtr path) => GdipCreatePath2(points == null ? null : ToF(points, count), types, count, brushMode, out path);
        internal static int GdipClonePath(IntPtr path, out IntPtr clonePath) { clonePath = IntPtr.Zero; var p = Path(path); if (p == null) return InvalidParameter; clonePath = NewHandle(p.Clone()); return Ok; }
        internal static int GdipDeletePath(HandleRef path) => Free(path);
        internal static int GdipResetPath(IntPtr path) => PathDo(path, p => p.Reset());
        internal static int GdipGetPointCount(IntPtr path, out int count) { var p = Path(path); count = p?.Count ?? 0; return p == null ? InvalidParameter : Ok; }

        internal static int GdipGetPathTypes(IntPtr path, byte[] types, int count)
        {
            var p = Path(path);
            if (p == null || types == null || count < p.Count) return InvalidParameter;
            p.Types.CopyTo(types);
            return Ok;
        }

        internal static int GdipGetPathPoints(IntPtr path, PointF[] points, int count)
        {
            var p = Path(path);
            if (p == null || points == null || count < p.Count) return InvalidParameter;
            p.Points.CopyTo(points);
            return Ok;
        }

        internal static int GdipGetPathPointsI(IntPtr path, Point[] points, int count)
        {
            var p = Path(path);
            if (p == null || points == null || count < p.Count) return InvalidParameter;
            for (int i = 0; i < p.Count; i++) points[i] = Point.Round(p.Points[i]);
            return Ok;
        }

        internal static int GdipGetPathFillMode(IntPtr path, out FillMode fillMode) { var p = Path(path); fillMode = p?.FillMode ?? 0; return p == null ? InvalidParameter : Ok; }
        internal static int GdipSetPathFillMode(IntPtr path, FillMode fillMode) => PathDo(path, p => p.FillMode = fillMode);
        internal static int GdipStartPathFigure(IntPtr path) => PathDo(path, p => p.StartFigure());
        internal static int GdipClosePathFigure(IntPtr path) => PathDo(path, p => p.CloseFigure());
        internal static int GdipClosePathFigures(IntPtr path) => PathDo(path, p => p.CloseAllFigures());
        internal static int GdipSetPathMarker(IntPtr path) => PathDo(path, p => { if (p.Types.Count > 0) p.Types[^1] |= (byte)PathPointType.PathMarker; });
        internal static int GdipClearPathMarkers(IntPtr path) => PathDo(path, p => { for (int i = 0; i < p.Types.Count; i++) p.Types[i] &= unchecked((byte)~PathPointType.PathMarker); });

        internal static int GdipReversePath(IntPtr path) => PathDo(path, p =>
        {
            // Reverse each figure, keeping the point types' structure (start, then the segments' own types reversed).
            var points = new List<PointF>();
            var types = new List<byte>();
            int start = 0;
            var figures = new List<(int Start, int End)>();
            for (int i = 1; i <= p.Count; i++)
                if (i == p.Count || (p.Types[i] & 7) == 0) { figures.Add((start, i - 1)); start = i; }
            figures.Reverse();
            foreach (var (s, e) in figures)
            {
                bool closed = (p.Types[e] & (byte)PathPointType.CloseSubpath) != 0;
                for (int i = e; i >= s; i--)
                {
                    points.Add(p.Points[i]);
                    byte t = i == e ? (byte)PathPointType.Start : (byte)(p.Types[i + 1] & 7);
                    types.Add(t);
                }
                if (closed) types[^1] |= (byte)PathPointType.CloseSubpath;
            }
            p.Points = points;
            p.Types = types;
        });

        internal static int GdipGetPathLastPoint(IntPtr path, out PointF lastPoint)
        {
            var p = Path(path);
            lastPoint = p != null && p.Count > 0 ? p.Points[^1] : default;
            return p == null || p.Count == 0 ? InvalidParameter : Ok;
        }

        internal static int GdipAddPathLine(IntPtr path, float x1, float y1, float x2, float y2) => PathDo(path, p => p.AddLine(new PointF(x1, y1), new PointF(x2, y2)));
        internal static int GdipAddPathLineI(IntPtr path, int x1, int y1, int x2, int y2) => GdipAddPathLine(path, x1, y1, x2, y2);
        internal static int GdipAddPathLine2(IntPtr path, PointF[] points, int count) => points == null || count < 1 ? InvalidParameter : PathDo(path, p => p.AddLines(points.Take(count).ToList()));
        internal static int GdipAddPathLine2I(IntPtr path, Point[] points, int count) => points == null ? InvalidParameter : GdipAddPathLine2(path, ToF(points, count), count);
        internal static int GdipAddPathArc(IntPtr path, float x, float y, float width, float height, float startAngle, float sweepAngle) => width <= 0 || height <= 0 ? InvalidParameter : PathDo(path, p => p.AddArc(new RectangleF(x, y, width, height), startAngle, sweepAngle));
        internal static int GdipAddPathArcI(IntPtr path, int x, int y, int width, int height, float startAngle, float sweepAngle) => GdipAddPathArc(path, x, y, width, height, startAngle, sweepAngle);
        internal static int GdipAddPathBezier(IntPtr path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) => PathDo(path, p => p.AddBeziers(new[] { new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3), new PointF(x4, y4) }));
        internal static int GdipAddPathBezierI(IntPtr path, int x1, int y1, int x2, int y2, int x3, int y3, int x4, int y4) => GdipAddPathBezier(path, x1, y1, x2, y2, x3, y3, x4, y4);
        internal static int GdipAddPathBeziers(IntPtr path, PointF[] points, int count) => points == null || count < 4 || (count - 1) % 3 != 0 ? InvalidParameter : PathDo(path, p => p.AddBeziers(points.Take(count).ToList()));
        internal static int GdipAddPathBeziersI(IntPtr path, Point[] points, int count) => points == null ? InvalidParameter : GdipAddPathBeziers(path, ToF(points, count), count);
        internal static int GdipAddPathCurve(IntPtr path, PointF[] points, int count) => GdipAddPathCurve3(path, points, count, 0, count - 1, 0.5f);
        internal static int GdipAddPathCurveI(IntPtr path, Point[] points, int count) => points == null ? InvalidParameter : GdipAddPathCurve(path, ToF(points, count), count);
        internal static int GdipAddPathCurve2(IntPtr path, PointF[] points, int count, float tension) => GdipAddPathCurve3(path, points, count, 0, count - 1, tension);
        internal static int GdipAddPathCurve2I(IntPtr path, Point[] points, int count, float tension) => points == null ? InvalidParameter : GdipAddPathCurve2(path, ToF(points, count), count, tension);

        internal static int GdipAddPathCurve3(IntPtr path, PointF[] points, int count, int offset, int numberOfSegments, float tension)
        {
            if (points == null || count < 2 || offset < 0 || numberOfSegments < 1 || offset + numberOfSegments >= count) return InvalidParameter;
            return PathDo(path, p => p.AddCurve(points.Take(count).ToList(), offset, numberOfSegments, tension));
        }

        internal static int GdipAddPathCurve3I(IntPtr path, Point[] points, int count, int offset, int numberOfSegments, float tension) => points == null ? InvalidParameter : GdipAddPathCurve3(path, ToF(points, count), count, offset, numberOfSegments, tension);
        internal static int GdipAddPathClosedCurve(IntPtr path, PointF[] points, int count) => GdipAddPathClosedCurve2(path, points, count, 0.5f);
        internal static int GdipAddPathClosedCurveI(IntPtr path, Point[] points, int count) => points == null ? InvalidParameter : GdipAddPathClosedCurve(path, ToF(points, count), count);
        internal static int GdipAddPathClosedCurve2(IntPtr path, PointF[] points, int count, float tension) => points == null || count < 3 ? InvalidParameter : PathDo(path, p => p.AddClosedCurve(points.Take(count).ToList(), tension));
        internal static int GdipAddPathClosedCurve2I(IntPtr path, Point[] points, int count, float tension) => points == null ? InvalidParameter : GdipAddPathClosedCurve2(path, ToF(points, count), count, tension);
        internal static int GdipAddPathRectangle(IntPtr path, float x, float y, float width, float height) => PathDo(path, p => p.AddRectangle(new RectangleF(x, y, width, height)));
        internal static int GdipAddPathRectangleI(IntPtr path, int x, int y, int width, int height) => GdipAddPathRectangle(path, x, y, width, height);
        internal static int GdipAddPathRectangles(IntPtr path, RectangleF[] rects, int count) => rects == null || count <= 0 ? InvalidParameter : PathDo(path, p => { foreach (var r in rects.Take(count)) p.AddRectangle(r); });
        internal static int GdipAddPathRectanglesI(IntPtr path, Rectangle[] rects, int count) => rects == null || count <= 0 ? InvalidParameter : PathDo(path, p => { foreach (var r in rects.Take(count)) p.AddRectangle(r); });
        internal static int GdipAddPathEllipse(IntPtr path, float x, float y, float width, float height) => PathDo(path, p => p.AddEllipse(new RectangleF(x, y, width, height)));
        internal static int GdipAddPathEllipseI(IntPtr path, int x, int y, int width, int height) => GdipAddPathEllipse(path, x, y, width, height);
        internal static int GdipAddPathPie(IntPtr path, float x, float y, float width, float height, float startAngle, float sweepAngle) => width <= 0 || height <= 0 ? InvalidParameter : PathDo(path, p => p.AddPie(new RectangleF(x, y, width, height), startAngle, sweepAngle));
        internal static int GdipAddPathPieI(IntPtr path, int x, int y, int width, int height, float startAngle, float sweepAngle) => GdipAddPathPie(path, x, y, width, height, startAngle, sweepAngle);
        internal static int GdipAddPathPolygon(IntPtr path, PointF[] points, int count) => points == null || count < 3 ? InvalidParameter : PathDo(path, p => p.AddPolygon(points.Take(count).ToList()));
        internal static int GdipAddPathPolygonI(IntPtr path, Point[] points, int count) => points == null ? InvalidParameter : GdipAddPathPolygon(path, ToF(points, count), count);

        internal static int GdipAddPathPath(IntPtr path, IntPtr addingPath, bool connect)
        {
            var other = Path(addingPath);
            if (other == null) return InvalidParameter;
            return PathDo(path, p => p.AddPath(other.Clone(), connect));
        }

        internal static int GdipTransformPath(IntPtr path, IntPtr matrix)
        {
            var m = Get<GpMatrix>(matrix);
            if (matrix != IntPtr.Zero && m == null) return InvalidParameter;
            return PathDo(path, p => { if (m != null) p.Transform(m); });
        }

        // Curves to line segments (Skia flattens to its tolerance; GDI+'s default flatness is 0.25).
        internal static int GdipFlattenPath(IntPtr path, IntPtr matrix, float floatness)
        {
            var p = Path(path);
            var m = Get<GpMatrix>(matrix);
            if (p == null) return InvalidParameter;
            if (m != null) p.Transform(m);
            if (!p.Types.Any(t => (t & 7) == 3)) return Ok;
            var flat = new GpPath { FillMode = p.FillMode };
            int i = 0;
            while (i < p.Count)
            {
                byte t = p.Types[i];
                if ((t & 7) == 3 && i > 0 && i + 2 < p.Count)
                {
                    var p0 = p.Points[i - 1]; var p1 = p.Points[i]; var p2 = p.Points[i + 1]; var p3 = p.Points[i + 2];
                    float length = Distance(p0, p1) + Distance(p1, p2) + Distance(p2, p3);
                    int steps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(length / Math.Max(floatness, 0.01f))), 2, 512);
                    for (int s = 1; s <= steps; s++)
                    {
                        float u = s / (float)steps, v = 1 - u;
                        var q = new PointF(v * v * v * p0.X + 3 * v * v * u * p1.X + 3 * v * u * u * p2.X + u * u * u * p3.X,
                                           v * v * v * p0.Y + 3 * v * v * u * p1.Y + 3 * v * u * u * p2.Y + u * u * u * p3.Y);
                        byte type = (byte)PathPointType.Line;
                        if (s == steps) type |= (byte)(p.Types[i + 2] & ~7);
                        flat.Points.Add(q);
                        flat.Types.Add(type);
                    }
                    i += 3;
                }
                else
                {
                    flat.Points.Add(p.Points[i]);
                    flat.Types.Add((t & 7) == 3 ? (byte)((t & ~7) | 1) : t);
                    i++;
                }
            }
            p.Points = flat.Points;
            p.Types = flat.Types;
            return Ok;
        }

        static float Distance(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        // The outline of the path stroked with the pen.
        internal static int GdipWidenPath(IntPtr path, IntPtr pen, IntPtr matrix, float flatness)
        {
            var p = Path(path);
            var pn = Get<GpPen>(pen);
            if (p == null || pn == null) return InvalidParameter;
            if (p.Count == 0) return OutOfMemory;
            var m = Get<GpMatrix>(matrix);
            using var sk = p.ToSkia();
            using var paint = new SKPaint();
            pn.Apply(paint, Math.Max(pn.Width, 1), SKMatrix.Identity, default);
            using var outline = new SKPath();
            paint.GetFillPath(sk, outline);
            var widened = GpPath.FromSkia(outline, FillMode.Winding);
            if (m != null) widened.Transform(m);
            p.Points = widened.Points;
            p.Types = widened.Types;
            p.FillMode = FillMode.Winding;
            return Ok;
        }

        // Maps the source rectangle onto the destination points (3: affine, 4: bilinear/perspective).
        internal static int GdipWarpPath(IntPtr path, IntPtr matrix, PointF[] points, int count, float srcx, float srcy, float srcwidth, float srcheight, WarpMode mode, float flatness)
        {
            var p = Path(path);
            if (p == null || points == null || (count != 3 && count != 4)) return InvalidParameter;
            if (srcwidth == 0 || srcheight == 0) return Ok;
            GdipFlattenPath(path, matrix, flatness);
            PointF ul = points[0], ur = points[1], ll = points[2], lr = count == 4 ? points[3] : new PointF(ur.X + ll.X - ul.X, ur.Y + ll.Y - ul.Y);
            for (int i = 0; i < p.Count; i++)
            {
                float u = (p.Points[i].X - srcx) / srcwidth, v = (p.Points[i].Y - srcy) / srcheight;
                p.Points[i] = new PointF(
                    (1 - u) * (1 - v) * ul.X + u * (1 - v) * ur.X + (1 - u) * v * ll.X + u * v * lr.X,
                    (1 - u) * (1 - v) * ul.Y + u * (1 - v) * ur.Y + (1 - u) * v * ll.Y + u * v * lr.Y);
            }
            return Ok;
        }

        internal static int GdipGetPathWorldBounds(IntPtr path, out RectangleF bounds, IntPtr matrix, IntPtr pen)
        {
            bounds = default;
            var p = Path(path);
            if (p == null) return InvalidParameter;
            var copy = p.Clone();
            var m = Get<GpMatrix>(matrix);
            if (m != null) copy.Transform(m);
            if (copy.Count == 0) return Ok;
            using var sk = copy.ToSkia();
            var r = sk.TightBounds;
            var pn = Get<GpPen>(pen);
            if (pn != null)
            {
                float half = Math.Max(pn.Width, 1) / 2;
                r.Inflate(half, half);
            }
            // GDI+ reports the control points' bounds for curves.
            float left = copy.Points.Min(q => q.X), top = copy.Points.Min(q => q.Y), right = copy.Points.Max(q => q.X), bottom = copy.Points.Max(q => q.Y);
            bounds = pn == null ? RectangleF.FromLTRB(left, top, right, bottom) : new RectangleF(r.Left, r.Top, r.Width, r.Height);
            return Ok;
        }

        internal static int GdipGetPathWorldBoundsI(IntPtr path, out Rectangle bounds, IntPtr matrix, IntPtr pen)
        {
            int status = GdipGetPathWorldBounds(path, out RectangleF b, matrix, pen);
            bounds = Rectangle.Round(b);
            return status;
        }

        internal static int GdipIsVisiblePathPoint(IntPtr path, float x, float y, IntPtr graphics, out bool result)
        {
            result = false;
            var p = Path(path);
            if (p == null) return InvalidParameter;
            using var sk = p.ToSkia();
            result = sk.Contains(x, y);
            return Ok;
        }

        internal static int GdipIsVisiblePathPointI(IntPtr path, int x, int y, IntPtr graphics, out bool result) => GdipIsVisiblePathPoint(path, x, y, graphics, out result);

        internal static int GdipIsOutlineVisiblePathPoint(IntPtr path, float x, float y, IntPtr pen, IntPtr graphics, out bool result)
        {
            result = false;
            var p = Path(path);
            var pn = Get<GpPen>(pen);
            if (p == null || pn == null) return InvalidParameter;
            using var sk = p.ToSkia();
            using var paint = new SKPaint();
            pn.Apply(paint, Math.Max(pn.Width, 1), SKMatrix.Identity, default);
            using var outline = new SKPath();
            paint.GetFillPath(sk, outline);
            result = outline.Contains(x, y);
            return Ok;
        }

        internal static int GdipIsOutlineVisiblePathPointI(IntPtr path, int x, int y, IntPtr pen, IntPtr graphics, out bool result) => GdipIsOutlineVisiblePathPoint(path, x, y, pen, graphics, out result);

        // Path iterators

        static GpPathIterator Iter(HandleRef i) => Get<GpPathIterator>(i);

        static List<(int Start, int End)> Subpaths(GpPath p)
        {
            var result = new List<(int, int)>();
            int start = 0;
            for (int i = 1; i <= p.Count; i++)
                if (i == p.Count || (p.Types[i] & 7) == 0) { if (i > start) result.Add((start, i - 1)); start = i; }
            return result;
        }

        internal static int GdipCreatePathIter(out IntPtr pathIter, HandleRef path) { pathIter = NewHandle(new GpPathIterator { Path = Get<GpPath>(path)?.Clone() ?? new GpPath() }); return Ok; }
        internal static int GdipDeletePathIter(HandleRef pathIter) => Free(pathIter);
        internal static int GdipPathIterGetCount(HandleRef pathIter, out int count) { var it = Iter(pathIter); count = it?.Path.Count ?? 0; return it == null ? InvalidParameter : Ok; }
        internal static int GdipPathIterGetSubpathCount(HandleRef pathIter, out int count) { var it = Iter(pathIter); count = it == null ? 0 : Subpaths(it.Path).Count; return it == null ? InvalidParameter : Ok; }
        internal static int GdipPathIterHasCurve(HandleRef pathIter, out bool hasCurve) { var it = Iter(pathIter); hasCurve = it?.Path.Types.Any(t => (t & 7) == 3) ?? false; return it == null ? InvalidParameter : Ok; }
        internal static int GdipPathIterRewind(HandleRef pathIter) { var it = Iter(pathIter); if (it == null) return InvalidParameter; it.Subpath = it.TypeIndex = it.Marker = 0; return Ok; }

        internal static int GdipPathIterNextSubpath(HandleRef pathIter, out int resultCount, out int startIndex, out int endIndex, out bool isClosed)
        {
            resultCount = startIndex = endIndex = 0;
            isClosed = false;
            var it = Iter(pathIter);
            if (it == null) return InvalidParameter;
            var subpaths = Subpaths(it.Path);
            if (it.Subpath >= subpaths.Count) return Ok;
            var (s, e) = subpaths[it.Subpath++];
            startIndex = s;
            endIndex = e;
            resultCount = e - s + 1;
            isClosed = (it.Path.Types[e] & (byte)PathPointType.CloseSubpath) != 0;
            it.TypeIndex = s;
            return Ok;
        }

        internal static int GdipPathIterNextSubpathPath(HandleRef pathIter, out int resultCount, HandleRef path, out bool isClosed)
        {
            int status = GdipPathIterNextSubpath(pathIter, out resultCount, out int s, out int e, out isClosed);
            var target = Get<GpPath>(path);
            if (status != Ok || target == null) return status;
            target.Reset();
            if (resultCount > 0)
            {
                var it = Iter(pathIter);
                target.Points.AddRange(it.Path.Points.Skip(s).Take(resultCount));
                target.Types.AddRange(it.Path.Types.Skip(s).Take(resultCount));
            }
            return Ok;
        }

        internal static int GdipPathIterNextPathType(HandleRef pathIter, out int resultCount, out byte pathType, out int startIndex, out int endIndex)
        {
            resultCount = startIndex = endIndex = 0;
            pathType = 0;
            var it = Iter(pathIter);
            if (it == null) return InvalidParameter;
            var p = it.Path;
            if (it.TypeIndex >= p.Count - 1) return Ok;
            int s = it.TypeIndex;
            byte type = (byte)(p.Types[s + 1] & 7);
            int e = s + 1;
            while (e + 1 < p.Count && (p.Types[e + 1] & 7) == type) e++;
            startIndex = s;
            endIndex = e;
            resultCount = e - s + 1;
            pathType = type;
            it.TypeIndex = e;
            return Ok;
        }

        internal static int GdipPathIterNextMarker(HandleRef pathIter, out int resultCount, out int startIndex, out int endIndex)
        {
            resultCount = startIndex = endIndex = 0;
            var it = Iter(pathIter);
            if (it == null) return InvalidParameter;
            var p = it.Path;
            if (it.Marker >= p.Count) return Ok;
            int s = it.Marker, e = s;
            while (e < p.Count - 1 && (p.Types[e] & (byte)PathPointType.PathMarker) == 0) e++;
            startIndex = s;
            endIndex = e;
            resultCount = e - s + 1;
            it.Marker = e + 1;
            return Ok;
        }

        internal static int GdipPathIterNextMarkerPath(HandleRef pathIter, out int resultCount, HandleRef path)
        {
            int status = GdipPathIterNextMarker(pathIter, out resultCount, out int s, out _);
            var target = Get<GpPath>(path);
            if (status != Ok || target == null) return status;
            target.Reset();
            var it = Iter(pathIter);
            target.Points.AddRange(it.Path.Points.Skip(s).Take(resultCount));
            target.Types.AddRange(it.Path.Types.Skip(s).Take(resultCount));
            return Ok;
        }

        internal static int GdipPathIterEnumerate(HandleRef pathIter, out int resultCount, PointF* points, byte* types, int count)
            => GdipPathIterCopyData(pathIter, out resultCount, points, types, 0, Math.Min(count, Iter(pathIter)?.Path.Count ?? 0) - 1);

        internal static int GdipPathIterCopyData(HandleRef pathIter, out int resultCount, PointF* points, byte* types, int startIndex, int endIndex)
        {
            resultCount = 0;
            var it = Iter(pathIter);
            if (it == null || points == null || types == null) return InvalidParameter;
            var p = it.Path;
            if (startIndex < 0 || endIndex >= p.Count || startIndex > endIndex) return Ok;
            for (int i = startIndex; i <= endIndex; i++)
            {
                points[i - startIndex] = p.Points[i];
                types[i - startIndex] = p.Types[i];
            }
            resultCount = endIndex - startIndex + 1;
            return Ok;
        }

        // Matrix queries

        internal static int GdipGetMatrixElements(HandleRef matrix, IntPtr m)
        {
            var mx = Get<GpMatrix>(matrix);
            if (mx == null || m == IntPtr.Zero) return InvalidParameter;
            Marshal.Copy(new[] { mx.M11, mx.M12, mx.M21, mx.M22, mx.Dx, mx.Dy }, 0, m, 6);
            return Ok;
        }

        internal static int GdipIsMatrixInvertible(HandleRef matrix, out int boolean) { var m = Get<GpMatrix>(matrix); boolean = m?.IsInvertible == true ? 1 : 0; return m == null ? InvalidParameter : Ok; }
        internal static int GdipIsMatrixIdentity(HandleRef matrix, out int boolean) { var m = Get<GpMatrix>(matrix); boolean = m?.IsIdentity == true ? 1 : 0; return m == null ? InvalidParameter : Ok; }
        internal static int GdipIsMatrixEqual(HandleRef matrix, HandleRef matrix2, out int boolean) { var a = Get<GpMatrix>(matrix); var b = Get<GpMatrix>(matrix2); boolean = a != null && b != null && a.Equals(b) ? 1 : 0; return a == null || b == null ? InvalidParameter : Ok; }
    }
    }
}
