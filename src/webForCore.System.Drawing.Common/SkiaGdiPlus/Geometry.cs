// webForCore: GDI+ matrices and paths for the managed GDI+ (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing
{
    // A GDI+ matrix: row vectors, [x y 1] * | m11 m12 0 | m21 m22 0 | dx dy 1 |.
    internal sealed class GpMatrix
    {
        public float M11 = 1, M12, M21, M22 = 1, Dx, Dy;

        public GpMatrix() { }
        public GpMatrix(float m11, float m12, float m21, float m22, float dx, float dy) { M11 = m11; M12 = m12; M21 = m21; M22 = m22; Dx = dx; Dy = dy; }

        public GpMatrix Clone() => (GpMatrix)MemberwiseClone();
        public void Set(GpMatrix m) { M11 = m.M11; M12 = m.M12; M21 = m.M21; M22 = m.M22; Dx = m.Dx; Dy = m.Dy; }
        public void Reset() => Set(new GpMatrix());
        // GDI+ treats elements within 0.0001 of the identity's as identity (Equals stays exact).
        public bool IsIdentity => NearOne(M11) && NearZero(M12) && NearZero(M21) && NearOne(M22) && NearZero(Dx) && NearZero(Dy);
        static bool NearOne(float v) => v >= 0.9999f && v <= 1.0001f;
        static bool NearZero(float v) => v >= -0.0001f && v <= 0.0001f;
        public float Determinant => M11 * M22 - M12 * M21;
        bool IsFinite => float.IsFinite(M11) && float.IsFinite(M12) && float.IsFinite(M21) && float.IsFinite(M22) && float.IsFinite(Dx) && float.IsFinite(Dy);
        public bool IsInvertible => IsFinite && Math.Abs(Determinant) > 1e-12f;

        // a * b: a applied first. Finite results that overflow a float saturate at float.MaxValue, as GDI+'s do;
        // infinite elements stay infinite.
        public static GpMatrix Product(GpMatrix a, GpMatrix b) => new GpMatrix(
            Sat((double)a.M11 * b.M11 + (double)a.M12 * b.M21), Sat((double)a.M11 * b.M12 + (double)a.M12 * b.M22),
            Sat((double)a.M21 * b.M11 + (double)a.M22 * b.M21), Sat((double)a.M21 * b.M12 + (double)a.M22 * b.M22),
            Sat((double)a.Dx * b.M11 + (double)a.Dy * b.M21 + b.Dx), Sat((double)a.Dx * b.M12 + (double)a.Dy * b.M22 + b.Dy));
        static float Sat(double v) => double.IsInfinity(v) ? (float)v : v > float.MaxValue ? float.MaxValue : v < -float.MaxValue ? -float.MaxValue : (float)v;

        // Prepend: m applied before this; Append: after.
        public void Multiply(GpMatrix m, MatrixOrder order) => Set(order == MatrixOrder.Prepend ? Product(m, this) : Product(this, m));
        public void Translate(float dx, float dy, MatrixOrder order) => Multiply(new GpMatrix(1, 0, 0, 1, dx, dy), order);
        public void Scale(float sx, float sy, MatrixOrder order) => Multiply(new GpMatrix(sx, 0, 0, sy, 0, 0), order);
        public void Shear(float sx, float sy, MatrixOrder order) => Multiply(new GpMatrix(1, sy, sx, 1, 0, 0), order);
        public void Rotate(float degrees, MatrixOrder order)
        {
            double r = degrees * Math.PI / 180;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            Multiply(new GpMatrix(c, s, -s, c, 0, 0), order);
        }

        public bool Invert()
        {
            float det = Determinant;
            if (!IsFinite || Math.Abs(det) < 1e-12f) return false;
            Set(new GpMatrix(M22 / det, -M12 / det, -M21 / det, M11 / det, (M21 * Dy - M22 * Dx) / det, (M12 * Dx - M11 * Dy) / det));
            return true;
        }

        public PointF Transform(PointF p) => new PointF(p.X * M11 + p.Y * M21 + Dx, p.X * M12 + p.Y * M22 + Dy);
        public PointF TransformVector(PointF p) => new PointF(p.X * M11 + p.Y * M21, p.X * M12 + p.Y * M22);
        public SKMatrix ToSkia() => new SKMatrix(M11, M21, Dx, M12, M22, Dy, 0, 0, 1);
        public bool Equals(GpMatrix m) => M11 == m.M11 && M12 == m.M12 && M21 == m.M21 && M22 == m.M22 && Dx == m.Dx && Dy == m.Dy;
    }

    // A GDI+ path: points with PathPointType bytes (Start 0, Line 1, Bezier 3; DashMode 0x10, PathMarker 0x20, CloseSubpath 0x80).
    internal sealed class GpPath
    {
        public List<PointF> Points = new List<PointF>();
        public List<byte> Types = new List<byte>();
        public FillMode FillMode;
        public bool StartNew = true;

        public GpPath Clone() => new GpPath { Points = new List<PointF>(Points), Types = new List<byte>(Types), FillMode = FillMode, StartNew = StartNew };
        public int Count => Points.Count;

        public void Reset()
        {
            Points.Clear();
            Types.Clear();
            FillMode = FillMode.Alternate;
            StartNew = true;
        }

        void Append(PointF p, byte type)
        {
            Points.Add(p);
            Types.Add(type);
        }

        // The first point of a new segment: a start if a figure begins, otherwise a line from the current point
        // (unless it is the current point).
        void Begin(PointF p)
        {
            if (StartNew || Points.Count == 0)
            {
                Append(p, (byte)PathPointType.Start);
                StartNew = false;
            }
            else if (Points[^1] != p)
            {
                Append(p, (byte)PathPointType.Line);
            }
        }

        public void StartFigure() => StartNew = true;

        public void CloseFigure()
        {
            if (Types.Count > 0 && !StartNew) Types[^1] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        public void CloseAllFigures()
        {
            for (int i = 0; i < Types.Count; i++)
                if (i + 1 == Types.Count || Types[i + 1] == (byte)PathPointType.Start) Types[i] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        public void AddLine(PointF a, PointF b)
        {
            Begin(a);
            Append(b, (byte)PathPointType.Line);
        }

        public void AddLines(IList<PointF> points)
        {
            if (points.Count == 0) return;
            Begin(points[0]);
            for (int i = 1; i < points.Count; i++) Append(points[i], (byte)PathPointType.Line);
        }

        public void AddBeziers(IList<PointF> points)
        {
            if (points.Count < 4) return;
            Begin(points[0]);
            for (int i = 1; i < points.Count; i++) Append(points[i], (byte)PathPointType.Bezier);
        }

        public void AddPolygon(IList<PointF> points)
        {
            if (points.Count < 3) return;
            Append(points[0], (byte)PathPointType.Start);
            for (int i = 1; i < points.Count; i++) Append(points[i], (byte)PathPointType.Line);
            Types[^1] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        public void AddRectangle(RectangleF r)
        {
            if (r.Width == 0 || r.Height == 0) return;
            AddPolygon(new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) });
        }

        public void AddEllipse(RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rx = r.Width / 2, ry = r.Height / 2;
            const float k = 0.5522847498f;
            Append(new PointF(cx + rx, cy), (byte)PathPointType.Start);
            var p = new[]
            {
                new PointF(cx + rx, cy + ry * k), new PointF(cx + rx * k, cy + ry), new PointF(cx, cy + ry),
                new PointF(cx - rx * k, cy + ry), new PointF(cx - rx, cy + ry * k), new PointF(cx - rx, cy),
                new PointF(cx - rx, cy - ry * k), new PointF(cx - rx * k, cy - ry), new PointF(cx, cy - ry),
                new PointF(cx + rx * k, cy - ry), new PointF(cx + rx, cy - ry * k), new PointF(cx + rx, cy),
            };
            foreach (var q in p) Append(q, (byte)PathPointType.Bezier);
            Types[^1] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        // Arc bezier points as GDI+ makes them: segments of at most 90 degrees, angles measured on the ellipse.
        public static List<PointF> ArcBeziers(RectangleF r, float startAngle, float sweepAngle)
        {
            var result = new List<PointF>();
            float rx = r.Width / 2, ry = r.Height / 2, cx = r.X + rx, cy = r.Y + ry;
            if (sweepAngle > 360) sweepAngle = 360;
            if (sweepAngle < -360) sweepAngle = -360;
            double EllipseAngle(double degrees)
            {
                // GDI+ angles are of the point on the ellipse, not of the parameter.
                double a = degrees * Math.PI / 180;
                return Math.Atan2(Math.Sin(a) / ry * rx, Math.Cos(a));
            }
            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / 90 - 1e-6));
            double step = sweepAngle / segments;
            for (int i = 0; i < segments; i++)
            {
                double a1 = EllipseAngle(startAngle + i * step), a2 = EllipseAngle(startAngle + (i + 1) * step);
                double d = a2 - a1;
                if (step > 0 && d < 0) d += 2 * Math.PI;
                if (step < 0 && d > 0) d -= 2 * Math.PI;
                if (Math.Abs(step) >= 360 / (double)segments - 1e-6 && Math.Abs(d) < 1e-9) d = Math.Sign(step) * Math.PI / 2;
                a2 = a1 + d;
                double k = 4.0 / 3 * Math.Tan(d / 4);
                var p0 = new PointF((float)(cx + rx * Math.Cos(a1)), (float)(cy + ry * Math.Sin(a1)));
                var p3 = new PointF((float)(cx + rx * Math.Cos(a2)), (float)(cy + ry * Math.Sin(a2)));
                var p1 = new PointF((float)(p0.X - k * rx * Math.Sin(a1)), (float)(p0.Y + k * ry * Math.Cos(a1)));
                var p2 = new PointF((float)(p3.X + k * rx * Math.Sin(a2)), (float)(p3.Y - k * ry * Math.Cos(a2)));
                if (i == 0) result.Add(p0);
                result.Add(p1); result.Add(p2); result.Add(p3);
            }
            return result;
        }

        public void AddArc(RectangleF r, float startAngle, float sweepAngle)
        {
            var b = ArcBeziers(r, startAngle, sweepAngle);
            Begin(b[0]);
            for (int i = 1; i < b.Count; i++) Append(b[i], (byte)PathPointType.Bezier);
        }

        public void AddPie(RectangleF r, float startAngle, float sweepAngle)
        {
            var center = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            Append(center, (byte)PathPointType.Start);
            var b = ArcBeziers(r, startAngle, sweepAngle);
            Append(b[0], (byte)PathPointType.Line);
            for (int i = 1; i < b.Count; i++) Append(b[i], (byte)PathPointType.Bezier);
            Types[^1] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        // Cardinal spline through the points, as beziers (GDI+: control points at tension/3 of the neighbours' chord).
        public static List<PointF> CurveBeziers(IList<PointF> pts, int offset, int segments, float tension, bool closed)
        {
            int n = pts.Count;
            var result = new List<PointF>();
            float t = tension / 3;
            PointF P(int i) => closed ? pts[((i % n) + n) % n] : pts[Math.Clamp(i, 0, n - 1)];
            int count = closed ? n : segments;
            result.Add(P(offset));
            for (int i = offset; i < offset + count; i++)
            {
                PointF p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
                result.Add(new PointF(p1.X + t * (p2.X - p0.X), p1.Y + t * (p2.Y - p0.Y)));
                result.Add(new PointF(p2.X - t * (p3.X - p1.X), p2.Y - t * (p3.Y - p1.Y)));
                result.Add(p2);
            }
            return result;
        }

        public void AddCurve(IList<PointF> points, int offset, int segments, float tension)
        {
            if (points.Count < 2) return;
            AddBeziers(CurveBeziers(points, offset, segments, tension, false));
        }

        public void AddClosedCurve(IList<PointF> points, float tension)
        {
            if (points.Count < 3) return;
            var b = CurveBeziers(points, 0, points.Count, tension, true);
            Append(b[0], (byte)PathPointType.Start);
            for (int i = 1; i < b.Count; i++) Append(b[i], (byte)PathPointType.Bezier);
            Types[^1] |= (byte)PathPointType.CloseSubpath;
            StartNew = true;
        }

        public void AddPath(GpPath other, bool connect)
        {
            for (int i = 0; i < other.Count; i++)
            {
                byte type = other.Types[i];
                if (i == 0 && connect && !StartNew && Points.Count > 0) type = (byte)PathPointType.Line;
                Append(other.Points[i], type);
            }
            StartNew = other.StartNew || (other.Types.Count > 0 && (other.Types[^1] & (byte)PathPointType.CloseSubpath) != 0);
            if (other.Count > 0 && !connect) StartNew = other.StartNew;
        }

        public void Transform(GpMatrix m)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = m.Transform(Points[i]);
        }

        public SKPath ToSkia()
        {
            var path = new SKPath { FillType = FillMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd };
            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                switch (Types[i] & 7)
                {
                    case 0: path.MoveTo(p.X, p.Y); break;
                    case 1: path.LineTo(p.X, p.Y); break;
                    case 3:
                        if (i + 2 < Points.Count)
                        {
                            path.CubicTo(p.X, p.Y, Points[i + 1].X, Points[i + 1].Y, Points[i + 2].X, Points[i + 2].Y);
                            i += 2;
                            if ((Types[i] & (byte)PathPointType.CloseSubpath) != 0) path.Close();
                            continue;
                        }
                        path.LineTo(p.X, p.Y);
                        break;
                }
                if ((Types[i] & (byte)PathPointType.CloseSubpath) != 0) path.Close();
            }
            return path;
        }

        public static GpPath FromSkia(SKPath path, FillMode mode)
        {
            var result = new GpPath { FillMode = mode };
            using var iterator = path.CreateRawIterator();
            var pts = new SKPoint[4];
            SKPathVerb verb;
            while ((verb = iterator.Next(pts)) != SKPathVerb.Done)
            {
                switch (verb)
                {
                    case SKPathVerb.Move: result.Append(new PointF(pts[0].X, pts[0].Y), (byte)PathPointType.Start); break;
                    case SKPathVerb.Line: result.Append(new PointF(pts[1].X, pts[1].Y), (byte)PathPointType.Line); break;
                    case SKPathVerb.Cubic:
                        for (int i = 1; i <= 3; i++) result.Append(new PointF(pts[i].X, pts[i].Y), (byte)PathPointType.Bezier);
                        break;
                    case SKPathVerb.Quad:
                        // degree elevation
                        var q0 = pts[0]; var q1 = pts[1]; var q2 = pts[2];
                        result.Append(new PointF(q0.X + 2f / 3 * (q1.X - q0.X), q0.Y + 2f / 3 * (q1.Y - q0.Y)), (byte)PathPointType.Bezier);
                        result.Append(new PointF(q2.X + 2f / 3 * (q1.X - q2.X), q2.Y + 2f / 3 * (q1.Y - q2.Y)), (byte)PathPointType.Bezier);
                        result.Append(new PointF(q2.X, q2.Y), (byte)PathPointType.Bezier);
                        break;
                    case SKPathVerb.Conic:
                        var conic = SKPath.ConvertConicToQuads(pts[0], pts[1], pts[2], iterator.ConicWeight(), 2);
                        for (int i = 1; i + 1 < conic.Length; i += 2)
                        {
                            var a = conic[i - 1]; var c = conic[i]; var e = conic[i + 1];
                            result.Append(new PointF(a.X + 2f / 3 * (c.X - a.X), a.Y + 2f / 3 * (c.Y - a.Y)), (byte)PathPointType.Bezier);
                            result.Append(new PointF(e.X + 2f / 3 * (c.X - e.X), e.Y + 2f / 3 * (c.Y - e.Y)), (byte)PathPointType.Bezier);
                            result.Append(new PointF(e.X, e.Y), (byte)PathPointType.Bezier);
                        }
                        break;
                    case SKPathVerb.Close:
                        if (result.Types.Count > 0) result.Types[^1] |= (byte)PathPointType.CloseSubpath;
                        break;
                }
            }
            result.StartNew = true;
            return result;
        }
    }
}
