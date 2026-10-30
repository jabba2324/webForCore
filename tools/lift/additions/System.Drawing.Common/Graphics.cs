// webForCore: GDI+ Graphics on an SKCanvas (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    // A Graphics drawing on a bitmap. GDI+ state is kept here and applied for each call: the device matrix is
    // world transform x page transform (unit and scale) x pixel offset, and the clip is a region in device space.
    internal sealed class GpGraphics : IDisposable
    {
        public GpImage Target;
        public SKCanvas Canvas;
        public float DpiX = 96, DpiY = 96;
        public GpMatrix World = new GpMatrix();
        public GraphicsUnit PageUnit = GraphicsUnit.Display;
        public float PageScale = 1;
        public GpRegion Clip = new GpRegion();
        public CompositingMode CompositingMode;
        public CompositingQuality CompositingQuality = CompositingQuality.Default;
        public SmoothingMode SmoothingMode = SmoothingMode.None;
        public PixelOffsetMode PixelOffsetMode = PixelOffsetMode.Default;
        public InterpolationMode InterpolationMode = InterpolationMode.Bilinear;
        public TextRenderingHint TextRenderingHint = TextRenderingHint.SystemDefault;
        public int TextContrast = 4;
        public Point RenderingOrigin;
        public readonly List<(uint Id, State State)> States = new List<(uint, State)>();
        public uint NextState = 1;

        internal sealed class State
        {
            public GpMatrix World;
            public GraphicsUnit PageUnit;
            public float PageScale;
            public GpRegion Clip;
            public CompositingMode CompositingMode;
            public CompositingQuality CompositingQuality;
            public SmoothingMode SmoothingMode;
            public PixelOffsetMode PixelOffsetMode;
            public InterpolationMode InterpolationMode;
            public TextRenderingHint TextRenderingHint;
            public int TextContrast;
            public Point RenderingOrigin;
            public GpMatrix Container;
        }

        // Containers: an extra transform (and clip) between the world transform and the page.
        public GpMatrix Container = new GpMatrix();

        public GpGraphics(GpImage target)
        {
            Target = target;
            Canvas = new SKCanvas(target.Pixels);
            DpiX = target.DpiX;
            DpiY = target.DpiY;
        }

        public void Dispose() => Canvas?.Dispose();

        public State Save() => new State
        {
            World = World.Clone(), PageUnit = PageUnit, PageScale = PageScale, Clip = Clip.Clone(), CompositingMode = CompositingMode,
            CompositingQuality = CompositingQuality, SmoothingMode = SmoothingMode, PixelOffsetMode = PixelOffsetMode,
            InterpolationMode = InterpolationMode, TextRenderingHint = TextRenderingHint, TextContrast = TextContrast,
            RenderingOrigin = RenderingOrigin, Container = Container.Clone(),
        };

        public void Restore(State s)
        {
            World = s.World; PageUnit = s.PageUnit; PageScale = s.PageScale; Clip = s.Clip; CompositingMode = s.CompositingMode;
            CompositingQuality = s.CompositingQuality; SmoothingMode = s.SmoothingMode; PixelOffsetMode = s.PixelOffsetMode;
            InterpolationMode = s.InterpolationMode; TextRenderingHint = s.TextRenderingHint; TextContrast = s.TextContrast;
            RenderingOrigin = s.RenderingOrigin; Container = s.Container;
        }

        // Pixels per unit.
        public static float UnitScale(GraphicsUnit unit, float dpi) => unit switch
        {
            GraphicsUnit.Point => dpi / 72,
            GraphicsUnit.Inch => dpi,
            GraphicsUnit.Document => dpi / 300,
            GraphicsUnit.Millimeter => dpi / 25.4f,
            _ => 1, // World, Display, Pixel (on a bitmap, Display is a pixel)
        };

        public GpMatrix PageMatrix => new GpMatrix(UnitScale(PageUnit, DpiX) * PageScale, 0, 0, UnitScale(PageUnit, DpiY) * PageScale, 0, 0);

        // World to page coordinates, through the containers.
        public GpMatrix WorldToPage => GpMatrix.Product(World, Container);
        public GpMatrix WorldToDevice => GpMatrix.Product(WorldToPage, PageMatrix);

        public bool HalfPixel => PixelOffsetMode == PixelOffsetMode.Half || PixelOffsetMode == PixelOffsetMode.HighQuality;
        public bool AntiAlias => SmoothingMode == SmoothingMode.AntiAlias || SmoothingMode == SmoothingMode.HighQuality;

        // GDI+ (PixelOffsetMode None/Default) puts pixel centres at integer coordinates, Skia at +0.5.
        public SKMatrix DeviceMatrix(bool forImage = false)
        {
            var m = WorldToDevice.ToSkia();
            if (!HalfPixel && !forImage) m = m.PostConcat(SKMatrix.CreateTranslation(0.5f, 0.5f));
            return m;
        }

        // Runs a drawing operation with the clip and the device matrix.
        public void Draw(Action<SKCanvas> draw, bool forImage = false)
        {
            Canvas.Save();
            Canvas.ResetMatrix();
            if (!Clip.Infinite) Canvas.ClipPath(Clip.Path, SKClipOperation.Intersect, false);
            Canvas.SetMatrix(DeviceMatrix(forImage));
            draw(Canvas);
            Canvas.Restore();
        }

        public SKPaint NewPaint() => new SKPaint
        {
            IsAntialias = AntiAlias,
            BlendMode = CompositingMode == CompositingMode.SourceCopy ? SKBlendMode.Src : SKBlendMode.SrcOver,
        };

        public SKSamplingOptions Sampling => InterpolationMode switch
        {
            InterpolationMode.NearestNeighbor => new SKSamplingOptions(SKFilterMode.Nearest),
            InterpolationMode.Bicubic or InterpolationMode.HighQualityBicubic or InterpolationMode.High => new SKSamplingOptions(SKCubicResampler.Mitchell),
            InterpolationMode.HighQualityBilinear => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => new SKSamplingOptions(SKFilterMode.Linear),
        };

        // Pen width in world units (a width of 0, or under a device pixel, is drawn one pixel wide as GDI+ does).
        public float PenWidth(GpPen pen)
        {
            if (pen.Unit == GraphicsUnit.World) return pen.Width;
            float pixels = pen.Width * UnitScale(pen.Unit, DpiX);
            var m = WorldToDevice;
            float scale = (float)Math.Sqrt(Math.Abs(m.Determinant));
            return scale > 0 ? pixels / scale : pixels;
        }

        public SKPaint Stroke(GpPen pen)
        {
            var paint = NewPaint();
            pen.Apply(paint, PenWidth(pen), DeviceMatrix(), RenderingOrigin);
            return paint;
        }

        public SKPaint Fill(GpBrush brush)
        {
            var paint = NewPaint();
            paint.Style = SKPaintStyle.Fill;
            brush.Apply(paint, DeviceMatrix(), RenderingOrigin);
            return paint;
        }
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        static GpGraphics G(HandleRef g) => Get<GpGraphics>(g);
        static GpGraphics G(IntPtr g) => Get<GpGraphics>(g);

        internal static int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics)
        {
            graphics = IntPtr.Zero;
            var img = Image(image);
            if (img == null) return InvalidParameter;
            if (GpImage.IsIndexed(img.Format)) return OutOfMemory; // GDI+: "A Graphics object cannot be created from an image that has an indexed pixel format."
            graphics = NewHandle(new GpGraphics(img));
            return Ok;
        }

        internal static int GdipDeleteGraphics(HandleRef graphics) => Free(graphics);

        // Not a bitmap: there are no HDCs, windows or X drawables here.
        internal static int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics) { graphics = IntPtr.Zero; return NotImplemented; }
        internal static int GdipCreateFromHWND(IntPtr hwnd, out IntPtr graphics) { graphics = IntPtr.Zero; return NotImplemented; }
        internal static int GdipGetDC(HandleRef graphics, out IntPtr hdc) { hdc = IntPtr.Zero; return NotImplemented; }
        internal static int GdipReleaseDC(HandleRef graphics, HandleRef hdc) => NotImplemented;

        internal static int GdipFlush(HandleRef graphics, FlushIntention intention)
        {
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            g.Canvas.Flush();
            g.Target.Pixels.NotifyPixelsChanged();
            return Ok;
        }

        // State

        static int Set(HandleRef graphics, Action<GpGraphics> set) { var g = G(graphics); if (g == null) return InvalidParameter; set(g); return Ok; }

        internal static int GdipSetCompositingMode(HandleRef graphics, CompositingMode compositingMode) => Set(graphics, g => g.CompositingMode = compositingMode);
        internal static int GdipGetCompositingMode(HandleRef graphics, out CompositingMode compositingMode) { var g = G(graphics); compositingMode = g?.CompositingMode ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetCompositingQuality(HandleRef graphics, CompositingQuality quality) => quality == CompositingQuality.Invalid ? InvalidParameter : Set(graphics, g => g.CompositingQuality = quality);
        internal static int GdipGetCompositingQuality(HandleRef graphics, out CompositingQuality quality) { var g = G(graphics); quality = g?.CompositingQuality ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetSmoothingMode(HandleRef graphics, SmoothingMode smoothingMode) => smoothingMode == SmoothingMode.Invalid ? InvalidParameter : Set(graphics, g => g.SmoothingMode = smoothingMode == SmoothingMode.Default ? SmoothingMode.None : smoothingMode == SmoothingMode.HighSpeed ? SmoothingMode.None : smoothingMode);
        internal static int GdipGetSmoothingMode(HandleRef graphics, out SmoothingMode smoothingMode) { var g = G(graphics); smoothingMode = g?.SmoothingMode ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetPixelOffsetMode(HandleRef graphics, PixelOffsetMode pixelOffsetMode) => pixelOffsetMode == PixelOffsetMode.Invalid ? InvalidParameter : Set(graphics, g => g.PixelOffsetMode = pixelOffsetMode);
        internal static int GdipGetPixelOffsetMode(HandleRef graphics, out PixelOffsetMode pixelOffsetMode) { var g = G(graphics); pixelOffsetMode = g?.PixelOffsetMode ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetInterpolationMode(HandleRef graphics, InterpolationMode interpolationMode) => interpolationMode == InterpolationMode.Invalid ? InvalidParameter : Set(graphics, g => g.InterpolationMode = interpolationMode == InterpolationMode.Default || interpolationMode == InterpolationMode.Low ? InterpolationMode.Bilinear : interpolationMode == InterpolationMode.High ? InterpolationMode.HighQualityBicubic : interpolationMode);
        internal static int GdipGetInterpolationMode(HandleRef graphics, out InterpolationMode interpolationMode) { var g = G(graphics); interpolationMode = g?.InterpolationMode ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetTextRenderingHint(HandleRef graphics, TextRenderingHint textRenderingHint) => Set(graphics, g => g.TextRenderingHint = textRenderingHint);
        internal static int GdipGetTextRenderingHint(HandleRef graphics, out TextRenderingHint textRenderingHint) { var g = G(graphics); textRenderingHint = g?.TextRenderingHint ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetTextContrast(HandleRef graphics, int textContrast) => textContrast < 0 || textContrast > 12 ? InvalidParameter : Set(graphics, g => g.TextContrast = textContrast);
        internal static int GdipGetTextContrast(HandleRef graphics, out int textContrast) { var g = G(graphics); textContrast = g?.TextContrast ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetRenderingOrigin(HandleRef graphics, int x, int y) => Set(graphics, g => g.RenderingOrigin = new Point(x, y));
        internal static int GdipGetRenderingOrigin(HandleRef graphics, out int x, out int y) { var g = G(graphics); x = g?.RenderingOrigin.X ?? 0; y = g?.RenderingOrigin.Y ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipGetPageUnit(HandleRef graphics, out GraphicsUnit unit) { var g = G(graphics); unit = g?.PageUnit ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipGetPageScale(HandleRef graphics, out float scale) { var g = G(graphics); scale = g?.PageScale ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipSetPageUnit(HandleRef graphics, GraphicsUnit unit) => unit == GraphicsUnit.World || unit < 0 || unit > GraphicsUnit.Millimeter ? InvalidParameter : Set(graphics, g => g.PageUnit = unit);
        internal static int GdipSetPageScale(HandleRef graphics, float scale) => scale <= 0 || scale > 1000000032 ? InvalidParameter : Set(graphics, g => g.PageScale = scale);
        internal static int GdipGetDpiX(HandleRef graphics, out float dpi) { var g = G(graphics); dpi = g?.DpiX ?? 0; return g == null ? InvalidParameter : Ok; }
        internal static int GdipGetDpiY(HandleRef graphics, out float dpi) { var g = G(graphics); dpi = g?.DpiY ?? 0; return g == null ? InvalidParameter : Ok; }

        // The colour comes in through the same variable; on a 32-bit surface the nearest colour is the colour itself.
        internal static int GdipGetNearestColor(IntPtr graphics, out int argb) { System.Runtime.CompilerServices.Unsafe.SkipInit(out argb); return G(graphics) == null ? InvalidParameter : Ok; }

        internal static int GdipSaveGraphics(HandleRef graphics, out int state)
        {
            state = 0;
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            uint id = g.NextState++;
            g.States.Add((id, g.Save()));
            state = (int)id;
            return Ok;
        }

        internal static int GdipRestoreGraphics(IntPtr graphics, uint graphicsState)
        {
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            int index = g.States.FindLastIndex(s => s.Id == graphicsState);
            if (index < 0) return Ok; // GDI+ ignores unknown states
            g.Restore(g.States[index].State);
            g.States.RemoveRange(index, g.States.Count - index);
            return Ok;
        }

        static int BeginContainer(GpGraphics g, GpMatrix container, out int state)
        {
            uint id = g.NextState++;
            g.States.Add((id, g.Save()));
            state = (int)id;
            // The new container: the current world transform becomes part of it, and the world transform starts as identity.
            g.Container = GpMatrix.Product(container, GpMatrix.Product(g.World, g.Container));
            g.World = new GpMatrix();
            return Ok;
        }

        internal static int GdipBeginContainer(HandleRef graphics, ref RectangleF dstRect, ref RectangleF srcRect, GraphicsUnit unit, out int state)
        {
            state = 0;
            var g = G(graphics);
            if (g == null || srcRect.Width == 0 || srcRect.Height == 0) return InvalidParameter;
            float scale = GpGraphics.UnitScale(unit, g.DpiX) / GpGraphics.UnitScale(g.PageUnit, g.DpiX) / g.PageScale;
            var m = new GpMatrix();
            m.Translate(-srcRect.X, -srcRect.Y, MatrixOrder.Append);
            m.Scale(dstRect.Width / srcRect.Width * scale, dstRect.Height / srcRect.Height * scale, MatrixOrder.Append);
            m.Translate(dstRect.X, dstRect.Y, MatrixOrder.Append);
            return BeginContainer(g, m, out state);
        }

        internal static int GdipBeginContainerI(HandleRef graphics, ref Rectangle dstRect, ref Rectangle srcRect, GraphicsUnit unit, out int state)
        {
            RectangleF d = dstRect, s = srcRect;
            return GdipBeginContainer(graphics, ref d, ref s, unit, out state);
        }

        internal static int GdipBeginContainer2(HandleRef graphics, out int state)
        {
            state = 0;
            var g = G(graphics);
            return g == null ? InvalidParameter : BeginContainer(g, new GpMatrix(), out state);
        }

        internal static int GdipEndContainer(HandleRef graphics, int state) => GdipRestoreGraphics(graphics.Handle, (uint)state);

        // World transform

        internal static int GdipSetWorldTransform(HandleRef graphics, HandleRef matrix)
        {
            var g = G(graphics);
            var m = Get<GpMatrix>(matrix);
            if (g == null || m == null) return InvalidParameter;
            if (!m.IsInvertible) return InvalidParameter;
            g.World = m.Clone();
            return Ok;
        }

        internal static int GdipResetWorldTransform(HandleRef graphics) => Set(graphics, g => g.World = new GpMatrix());

        internal static int GdipMultiplyWorldTransform(HandleRef graphics, HandleRef matrix, MatrixOrder order)
        {
            var g = G(graphics);
            var m = Get<GpMatrix>(matrix);
            if (g == null || m == null) return InvalidParameter;
            if (!m.IsInvertible) return InvalidParameter;
            g.World.Multiply(m, order);
            return Ok;
        }

        internal static int GdipTranslateWorldTransform(HandleRef graphics, float dx, float dy, MatrixOrder order) => Set(graphics, g => g.World.Translate(dx, dy, order));
        internal static int GdipScaleWorldTransform(HandleRef graphics, float sx, float sy, MatrixOrder order) => Set(graphics, g => g.World.Scale(sx, sy, order));
        internal static int GdipRotateWorldTransform(HandleRef graphics, float angle, MatrixOrder order) => Set(graphics, g => g.World.Rotate(angle, order));
        internal static int GdipGetWorldTransform(HandleRef graphics, HandleRef matrix) { var g = G(graphics); var m = Get<GpMatrix>(matrix); if (g == null || m == null) return InvalidParameter; m.Set(g.World); return Ok; }

        // CoordinateSpace: World 0, Page 1, Device 2.
        static GpMatrix SpaceToDevice(GpGraphics g, int space) => space switch
        {
            0 => g.WorldToDevice,
            1 => g.PageMatrix,
            _ => new GpMatrix(),
        };

        internal static int GdipTransformPoints(HandleRef graphics, int destSpace, int srcSpace, PointF* points, int count)
        {
            var g = G(graphics);
            if (g == null || points == null || count <= 0 || destSpace < 0 || destSpace > 2 || srcSpace < 0 || srcSpace > 2) return InvalidParameter;
            var toDevice = SpaceToDevice(g, srcSpace);
            var fromDevice = SpaceToDevice(g, destSpace).Clone();
            if (!fromDevice.Invert()) return InvalidParameter;
            var m = GpMatrix.Product(toDevice, fromDevice);
            for (int i = 0; i < count; i++) points[i] = m.Transform(points[i]);
            return Ok;
        }

        internal static int GdipTransformPointsI(HandleRef graphics, int destSpace, int srcSpace, Point* points, int count)
        {
            var f = new PointF[Math.Max(count, 0)];
            for (int i = 0; i < count; i++) f[i] = points[i];
            fixed (PointF* p = f)
            {
                int status = GdipTransformPoints(graphics, destSpace, srcSpace, p, count);
                for (int i = 0; i < count; i++) points[i] = Point.Round(f[i]);
                return status;
            }
        }

        // Clipping (kept in device space)

        static int SetClip(GpGraphics g, GpRegion worldRegion, CombineMode mode)
        {
            if (mode < CombineMode.Replace || mode > CombineMode.Complement) return InvalidParameter;
            var device = worldRegion.Clone();
            device.Transform(g.WorldToDevice.ToSkia());
            g.Clip = g.Clip.Clone();
            g.Clip.Combine(device, mode);
            return Ok;
        }

        internal static int GdipSetClipRect(HandleRef graphics, float x, float y, float width, float height, CombineMode mode)
        {
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            if (width < 0) { x += width; width = -width; }
            if (height < 0) { y += height; height = -height; }
            return SetClip(g, GpRegion.FromRect(new RectangleF(x, y, width, height)), mode);
        }

        internal static int GdipSetClipRectI(HandleRef graphics, int x, int y, int width, int height, CombineMode mode) => GdipSetClipRect(graphics, x, y, width, height, mode);

        internal static int GdipSetClipPath(HandleRef graphics, HandleRef path, CombineMode mode)
        {
            var g = G(graphics);
            var p = Get<GpPath>(path);
            if (g == null || p == null) return InvalidParameter;
            using var sk = p.ToSkia();
            return SetClip(g, GpRegion.FromPath(sk), mode);
        }

        internal static int GdipSetClipRegion(HandleRef graphics, HandleRef region, CombineMode mode)
        {
            var g = G(graphics);
            var r = Get<GpRegion>(region);
            if (g == null || r == null) return InvalidParameter;
            return SetClip(g, r, mode);
        }

        internal static int GdipSetClipGraphics(HandleRef graphics, HandleRef srcgraphics, CombineMode mode)
        {
            var g = G(graphics);
            var src = G(srcgraphics);
            if (g == null || src == null) return InvalidParameter;
            g.Clip = g.Clip.Clone();
            g.Clip.Combine(src.Clip, mode);
            return Ok;
        }

        internal static int GdipResetClip(HandleRef graphics) => Set(graphics, g => g.Clip = new GpRegion());

        internal static int GdipTranslateClip(HandleRef graphics, float dx, float dy)
        {
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            var v = g.WorldToDevice.TransformVector(new PointF(dx, dy));
            g.Clip = g.Clip.Clone();
            g.Clip.Transform(SKMatrix.CreateTranslation(v.X, v.Y));
            return Ok;
        }

        // The clip in world coordinates.
        static GpRegion WorldClip(GpGraphics g)
        {
            var clip = g.Clip.Clone();
            var inverse = g.WorldToDevice.Clone();
            if (inverse.Invert()) clip.Transform(inverse.ToSkia());
            return clip;
        }

        internal static int GdipGetClip(HandleRef graphics, HandleRef region)
        {
            var g = G(graphics);
            var r = Get<GpRegion>(region);
            if (g == null || r == null) return InvalidParameter;
            r.Combine(WorldClip(g), CombineMode.Replace);
            return Ok;
        }

        internal static int GdipGetClipBounds(HandleRef graphics, out RectangleF rect)
        {
            var g = G(graphics);
            rect = g == null ? default : WorldClip(g).Bounds;
            return g == null ? InvalidParameter : Ok;
        }

        internal static int GdipIsClipEmpty(HandleRef graphics, out bool result) { var g = G(graphics); result = g?.Clip.IsEmpty ?? false; return g == null ? InvalidParameter : Ok; }

        // The visible clip: the clip within the bitmap.
        static GpRegion VisibleClip(GpGraphics g)
        {
            var visible = GpRegion.FromRect(new RectangleF(0, 0, g.Target.Width, g.Target.Height));
            visible.Combine(g.Clip, CombineMode.Intersect);
            return visible;
        }

        internal static int GdipGetVisibleClipBounds(HandleRef graphics, out RectangleF rect)
        {
            rect = default;
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            var visible = VisibleClip(g);
            var inverse = g.WorldToDevice.Clone();
            if (inverse.Invert()) visible.Transform(inverse.ToSkia());
            rect = visible.Bounds;
            return Ok;
        }

        internal static int GdipIsVisibleClipEmpty(HandleRef graphics, out bool result) { var g = G(graphics); result = g != null && VisibleClip(g).IsEmpty; return g == null ? InvalidParameter : Ok; }

        internal static int GdipIsVisiblePoint(HandleRef graphics, float x, float y, out bool result)
        {
            result = false;
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            var p = g.WorldToDevice.Transform(new PointF(x, y));
            result = VisibleClip(g).Contains(p.X, p.Y);
            return Ok;
        }

        internal static int GdipIsVisiblePointI(HandleRef graphics, int x, int y, out bool result) => GdipIsVisiblePoint(graphics, x, y, out result);

        internal static int GdipIsVisibleRect(HandleRef graphics, float x, float y, float width, float height, out bool result)
        {
            result = false;
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            var r = GpRegion.FromRect(new RectangleF(x, y, width, height));
            r.Transform(g.WorldToDevice.ToSkia());
            result = VisibleClip(g).Intersects(r.Bounds);
            return Ok;
        }

        internal static int GdipIsVisibleRectI(HandleRef graphics, int x, int y, int width, int height, out bool result) => GdipIsVisibleRect(graphics, x, y, width, height, out result);

        // Drawing

        internal static int GdipGraphicsClear(HandleRef graphics, int argb)
        {
            var g = G(graphics);
            if (g == null) return InvalidParameter;
            g.Canvas.Save();
            g.Canvas.ResetMatrix();
            if (!g.Clip.Infinite) g.Canvas.ClipPath(g.Clip.Path, SKClipOperation.Intersect, false);
            var color = new SKColor((uint)argb);
            if (!GpImage.HasAlpha(g.Target.Format)) color = color.WithAlpha(255);
            g.Canvas.DrawColor(color, SKBlendMode.Src);
            g.Canvas.Restore();
            return Ok;
        }

        static int StrokePath(HandleRef graphics, HandleRef pen, Action<SKPath> build)
        {
            var g = G(graphics);
            var p = Get<GpPen>(pen);
            if (g == null || p == null) return InvalidParameter;
            using var path = new SKPath();
            build(path);
            using var paint = g.Stroke(p);
            g.Draw(c => c.DrawPath(path, paint));
            DrawCaps(g, p, path);
            return Ok;
        }

        static int FillPath(GpGraphics g, GpBrush b, SKPath path)
        {
            if (g == null || b == null) return InvalidParameter;
            using var paint = g.Fill(b);
            g.Draw(c => c.DrawPath(path, paint));
            return Ok;
        }

        // (No default value of a Skia enum: the strong-name step's Cecil cannot resolve SkiaSharp.)
        static int FillPath(HandleRef graphics, HandleRef brush, Action<SKPath> build) => FillPath(graphics, brush, build, SKPathFillType.Winding);

        static int FillPath(HandleRef graphics, HandleRef brush, Action<SKPath> build, SKPathFillType fill)
        {
            using var path = new SKPath { FillType = fill };
            build(path);
            return FillPath(G(graphics), Get<GpBrush>(brush), path);
        }

        // Anchor caps (arrow, round, square, diamond anchors) at the ends of an open path.
        static void DrawCaps(GpGraphics g, GpPen pen, SKPath path)
        {
            bool anchors(LineCap c) => c == LineCap.ArrowAnchor || c == LineCap.RoundAnchor || c == LineCap.SquareAnchor || c == LineCap.DiamondAnchor
                || c == LineCap.Custom;
            if (!anchors(pen.StartCap) && !anchors(pen.EndCap)) return;
            var points = path.Points;
            if (points.Length < 2) return;
            float width = Math.Max(g.PenWidth(pen), 1) * 2;
            void Cap(LineCap cap, GpCustomLineCap custom, SKPoint at, SKPoint from)
            {
                if (!anchors(cap)) return;
                var dir = at - from;
                float len = dir.Length;
                if (len == 0) return;
                var u = new SKPoint(dir.X / len, dir.Y / len);
                var n = new SKPoint(-u.Y, u.X);
                using var shape = new SKPath();
                if (cap == LineCap.ArrowAnchor || custom?.IsArrow == true)
                {
                    float h = custom?.IsArrow == true ? custom.ArrowHeight * width / 2 : width;
                    float w = custom?.IsArrow == true ? custom.ArrowWidth * width / 4 : width / 2;
                    shape.MoveTo(at);
                    shape.LineTo(at - new SKPoint(u.X * h, u.Y * h) + new SKPoint(n.X * w, n.Y * w));
                    shape.LineTo(at - new SKPoint(u.X * h, u.Y * h) - new SKPoint(n.X * w, n.Y * w));
                    shape.Close();
                }
                else if (cap == LineCap.RoundAnchor) shape.AddCircle(at.X, at.Y, width / 2);
                else if (cap == LineCap.SquareAnchor) shape.AddRect(new SKRect(at.X - width / 2, at.Y - width / 2, at.X + width / 2, at.Y + width / 2));
                else if (cap == LineCap.DiamondAnchor)
                {
                    shape.MoveTo(at + new SKPoint(u.X * width / 2, u.Y * width / 2));
                    shape.LineTo(at + new SKPoint(n.X * width / 2, n.Y * width / 2));
                    shape.LineTo(at - new SKPoint(u.X * width / 2, u.Y * width / 2));
                    shape.LineTo(at - new SKPoint(n.X * width / 2, n.Y * width / 2));
                    shape.Close();
                }
                else return;
                using var paint = g.Stroke(pen);
                paint.Style = custom?.IsArrow == true && !custom.Filled ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
                paint.PathEffect = null;
                g.Draw(c => c.DrawPath(shape, paint));
            }
            Cap(pen.StartCap, pen.CustomStart, points[0], points[1]);
            Cap(pen.EndCap, pen.CustomEnd, points[^1], points[^2]);
        }

        static SKPoint[] Pts(PointF* points, int count) { var r = new SKPoint[count]; for (int i = 0; i < count; i++) r[i] = new SKPoint(points[i].X, points[i].Y); return r; }
        static SKPoint[] Pts(Point* points, int count) { var r = new SKPoint[count]; for (int i = 0; i < count; i++) r[i] = new SKPoint(points[i].X, points[i].Y); return r; }
        static PointF[] PtsF(Point* points, int count) { var r = new PointF[count]; for (int i = 0; i < count; i++) r[i] = points[i]; return r; }
        static PointF[] PtsF(PointF* points, int count) { var r = new PointF[count]; for (int i = 0; i < count; i++) r[i] = points[i]; return r; }

        static void AddGpPath(SKPath path, GpPath gp) { using var sk = gp.ToSkia(); path.AddPath(sk); }

        internal static int GdipDrawLine(HandleRef graphics, HandleRef pen, float x1, float y1, float x2, float y2) => StrokePath(graphics, pen, p => { p.MoveTo(x1, y1); p.LineTo(x2, y2); });
        internal static int GdipDrawLineI(HandleRef graphics, HandleRef pen, int x1, int y1, int x2, int y2) => GdipDrawLine(graphics, pen, x1, y1, x2, y2);

        internal static int GdipDrawLines(HandleRef graphics, HandleRef pen, PointF* points, int count)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return StrokePath(graphics, pen, p => p.AddPoly(pts, false));
        }

        internal static int GdipDrawLinesI(HandleRef graphics, HandleRef pen, Point* points, int count)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return StrokePath(graphics, pen, p => p.AddPoly(pts, false));
        }

        internal static int GdipDrawRectangle(HandleRef graphics, HandleRef pen, float x, float y, float width, float height)
        {
            if (width < 0 || height < 0) return Ok; // GDI+ draws nothing
            return StrokePath(graphics, pen, p => p.AddRect(new SKRect(x, y, x + width, y + height)));
        }

        internal static int GdipDrawRectangleI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height) => GdipDrawRectangle(graphics, pen, x, y, width, height);

        internal static int GdipDrawRectangles(HandleRef graphics, HandleRef pen, RectangleF* rects, int count)
        {
            if (rects == null || count <= 0) return InvalidParameter;
            var list = new RectangleF[count];
            for (int i = 0; i < count; i++) list[i] = rects[i];
            return StrokePath(graphics, pen, p => { foreach (var r in list) if (r.Width >= 0 && r.Height >= 0) p.AddRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom)); });
        }

        internal static int GdipDrawRectanglesI(HandleRef graphics, HandleRef pen, Rectangle* rects, int count)
        {
            if (rects == null || count <= 0) return InvalidParameter;
            var f = new RectangleF[count];
            for (int i = 0; i < count; i++) f[i] = rects[i];
            fixed (RectangleF* p = f) return GdipDrawRectangles(graphics, pen, p, count);
        }

        internal static int GdipDrawEllipse(HandleRef graphics, HandleRef pen, float x, float y, float width, float height) => StrokePath(graphics, pen, p => p.AddOval(new SKRect(x, y, x + width, y + height)));
        internal static int GdipDrawEllipseI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height) => GdipDrawEllipse(graphics, pen, x, y, width, height);

        internal static int GdipDrawArc(HandleRef graphics, HandleRef pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            if (width <= 0 || height <= 0) return InvalidParameter;
            return StrokePath(graphics, pen, p => { var gp = new GpPath(); gp.AddArc(new RectangleF(x, y, width, height), startAngle, sweepAngle); AddGpPath(p, gp); });
        }

        internal static int GdipDrawArcI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height, float startAngle, float sweepAngle) => GdipDrawArc(graphics, pen, x, y, width, height, startAngle, sweepAngle);

        internal static int GdipDrawPie(HandleRef graphics, HandleRef pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            if (width <= 0 || height <= 0) return InvalidParameter;
            return StrokePath(graphics, pen, p => { var gp = new GpPath(); gp.AddPie(new RectangleF(x, y, width, height), startAngle, sweepAngle); AddGpPath(p, gp); });
        }

        internal static int GdipDrawPieI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height, float startAngle, float sweepAngle) => GdipDrawPie(graphics, pen, x, y, width, height, startAngle, sweepAngle);

        internal static int GdipDrawBezier(HandleRef graphics, HandleRef pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
            => StrokePath(graphics, pen, p => { p.MoveTo(x1, y1); p.CubicTo(x2, y2, x3, y3, x4, y4); });

        internal static int GdipDrawPolygon(HandleRef graphics, HandleRef pen, PointF* points, int count)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return StrokePath(graphics, pen, p => p.AddPoly(pts, true));
        }

        internal static int GdipDrawPolygonI(HandleRef graphics, HandleRef pen, Point* points, int count)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return StrokePath(graphics, pen, p => p.AddPoly(pts, true));
        }

        internal static int GdipDrawPath(HandleRef graphics, HandleRef pen, HandleRef path)
        {
            var gp = Get<GpPath>(path);
            if (gp == null) return InvalidParameter;
            return StrokePath(graphics, pen, p => AddGpPath(p, gp));
        }

        static int DrawCurve(HandleRef graphics, HandleRef pen, PointF[] points, int offset, int segments, float tension)
        {
            if (points.Length < 2 || offset < 0 || segments < 1 || offset + segments >= points.Length + (segments == 0 ? 1 : 0) && offset + segments > points.Length - 1) return InvalidParameter;
            return StrokePath(graphics, pen, p => { var gp = new GpPath(); gp.AddCurve(points, offset, segments, tension); AddGpPath(p, gp); });
        }

        internal static int GdipDrawCurve(HandleRef graphics, HandleRef pen, PointF* points, int count) => DrawCurve(graphics, pen, PtsF(points, count), 0, count - 1, 0.5f);
        internal static int GdipDrawCurveI(HandleRef graphics, HandleRef pen, Point* points, int count) => DrawCurve(graphics, pen, PtsF(points, count), 0, count - 1, 0.5f);
        internal static int GdipDrawCurve2(HandleRef graphics, HandleRef pen, PointF* points, int count, float tension) => DrawCurve(graphics, pen, PtsF(points, count), 0, count - 1, tension);
        internal static int GdipDrawCurve2I(HandleRef graphics, HandleRef pen, Point* points, int count, float tension) => DrawCurve(graphics, pen, PtsF(points, count), 0, count - 1, tension);
        internal static int GdipDrawCurve3(HandleRef graphics, HandleRef pen, PointF* points, int count, int offset, int numberOfSegments, float tension) => DrawCurve(graphics, pen, PtsF(points, count), offset, numberOfSegments, tension);
        internal static int GdipDrawCurve3I(HandleRef graphics, HandleRef pen, Point* points, int count, int offset, int numberOfSegments, float tension) => DrawCurve(graphics, pen, PtsF(points, count), offset, numberOfSegments, tension);

        static int DrawClosedCurve(HandleRef graphics, HandleRef pen, PointF[] points, float tension)
        {
            if (points.Length < 3) return InvalidParameter;
            return StrokePath(graphics, pen, p => { var gp = new GpPath(); gp.AddClosedCurve(points, tension); AddGpPath(p, gp); });
        }

        internal static int GdipDrawClosedCurve(HandleRef graphics, HandleRef pen, PointF* points, int count) => DrawClosedCurve(graphics, pen, PtsF(points, count), 0.5f);
        internal static int GdipDrawClosedCurveI(HandleRef graphics, HandleRef pen, Point* points, int count) => DrawClosedCurve(graphics, pen, PtsF(points, count), 0.5f);
        internal static int GdipDrawClosedCurve2(HandleRef graphics, HandleRef pen, PointF* points, int count, float tension) => DrawClosedCurve(graphics, pen, PtsF(points, count), tension);
        internal static int GdipDrawClosedCurve2I(HandleRef graphics, HandleRef pen, Point* points, int count, float tension) => DrawClosedCurve(graphics, pen, PtsF(points, count), tension);

        // Fills

        internal static int GdipFillRectangle(HandleRef graphics, HandleRef brush, float x, float y, float width, float height)
        {
            if (width < 0 || height < 0) return Ok;
            return FillPath(graphics, brush, p => p.AddRect(new SKRect(x, y, x + width, y + height)));
        }

        internal static int GdipFillRectangleI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height) => GdipFillRectangle(graphics, brush, x, y, width, height);

        internal static int GdipFillRectangles(HandleRef graphics, HandleRef brush, RectangleF* rects, int count)
        {
            if (rects == null || count <= 0) return InvalidParameter;
            var list = new RectangleF[count];
            for (int i = 0; i < count; i++) list[i] = rects[i];
            return FillPath(graphics, brush, p => { foreach (var r in list) if (r.Width > 0 && r.Height > 0) p.AddRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom)); });
        }

        internal static int GdipFillRectanglesI(HandleRef graphics, HandleRef brush, Rectangle* rects, int count)
        {
            if (rects == null || count <= 0) return InvalidParameter;
            var f = new RectangleF[count];
            for (int i = 0; i < count; i++) f[i] = rects[i];
            fixed (RectangleF* p = f) return GdipFillRectangles(graphics, brush, p, count);
        }

        internal static int GdipFillEllipse(HandleRef graphics, HandleRef brush, float x, float y, float width, float height) => FillPath(graphics, brush, p => p.AddOval(new SKRect(x, y, x + width, y + height)));
        internal static int GdipFillEllipseI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height) => GdipFillEllipse(graphics, brush, x, y, width, height);

        internal static int GdipFillPie(HandleRef graphics, HandleRef brush, float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            if (width <= 0 || height <= 0) return InvalidParameter;
            return FillPath(graphics, brush, p => { var gp = new GpPath(); gp.AddPie(new RectangleF(x, y, width, height), startAngle, sweepAngle); AddGpPath(p, gp); });
        }

        internal static int GdipFillPieI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height, float startAngle, float sweepAngle) => GdipFillPie(graphics, brush, x, y, width, height, startAngle, sweepAngle);

        internal static int GdipFillPolygon(HandleRef graphics, HandleRef brush, PointF* points, int count, FillMode brushMode)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return FillPath(graphics, brush, p => p.AddPoly(pts, true), brushMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd);
        }

        internal static int GdipFillPolygonI(HandleRef graphics, HandleRef brush, Point* points, int count, FillMode brushMode)
        {
            if (points == null || count < 2) return InvalidParameter;
            var pts = Pts(points, count);
            return FillPath(graphics, brush, p => p.AddPoly(pts, true), brushMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd);
        }

        internal static int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path)
        {
            var gp = Get<GpPath>(path);
            if (gp == null) return InvalidParameter;
            using var sk = gp.ToSkia();
            return FillPath(G(graphics), Get<GpBrush>(brush), sk);
        }

        internal static int GdipFillRegion(HandleRef graphics, HandleRef brush, HandleRef region)
        {
            var r = Get<GpRegion>(region);
            if (r == null) return InvalidParameter;
            using var path = r.AsPath();
            return FillPath(G(graphics), Get<GpBrush>(brush), path);
        }

        static int FillClosedCurve(HandleRef graphics, HandleRef brush, PointF[] points, float tension, FillMode mode)
        {
            if (points.Length < 3) return InvalidParameter;
            var gp = new GpPath { FillMode = mode };
            gp.AddClosedCurve(points, tension);
            using var sk = gp.ToSkia();
            return FillPath(G(graphics), Get<GpBrush>(brush), sk);
        }

        internal static int GdipFillClosedCurve(HandleRef graphics, HandleRef brush, PointF* points, int count) => FillClosedCurve(graphics, brush, PtsF(points, count), 0.5f, FillMode.Alternate);
        internal static int GdipFillClosedCurveI(HandleRef graphics, HandleRef brush, Point* points, int count) => FillClosedCurve(graphics, brush, PtsF(points, count), 0.5f, FillMode.Alternate);
        internal static int GdipFillClosedCurve2(HandleRef graphics, HandleRef brush, PointF* points, int count, float tension, FillMode mode) => FillClosedCurve(graphics, brush, PtsF(points, count), tension, mode);
        internal static int GdipFillClosedCurve2I(HandleRef graphics, HandleRef brush, Point* points, int count, float tension, FillMode mode) => FillClosedCurve(graphics, brush, PtsF(points, count), tension, mode);

        // Images

        // Draws a source rectangle (pixels) of the image into a destination parallelogram given by its upper-left,
        // upper-right and lower-left corners.
        static int DrawImage(GpGraphics g, GpImage image, RectangleF src, PointF ul, PointF ur, PointF ll, GpImageAttributes attributes)
        {
            if (g == null || image == null) return InvalidParameter;
            if (src.Width == 0 || src.Height == 0) return Ok;
            var pixels = attributes != null ? attributes.Apply(image.Pixels, ColorAdjustType.Bitmap) : image.Pixels;
            try
            {
                using var skImage = SKImage.FromBitmap(pixels);
                var map = new SKMatrix(
                    (ur.X - ul.X) / src.Width, (ll.X - ul.X) / src.Height, ul.X,
                    (ur.Y - ul.Y) / src.Width, (ll.Y - ul.Y) / src.Height, ul.Y, 0, 0, 1);
                map = SKMatrix.Concat(map, SKMatrix.CreateTranslation(-src.X, -src.Y));
                using var paint = g.NewPaint();
                paint.IsAntialias = g.AntiAlias || g.InterpolationMode != InterpolationMode.NearestNeighbor;
                g.Draw(c =>
                {
                    c.Concat(map);
                    c.DrawImage(skImage, new SKRect(src.Left, src.Top, src.Right, src.Bottom), new SKRect(src.Left, src.Top, src.Right, src.Bottom), g.Sampling, paint);
                }, forImage: true);
            }
            finally
            {
                if (pixels != image.Pixels) pixels.Dispose();
            }
            return Ok;
        }

        static RectangleF SourcePixels(GpImage image, RectangleF src, GraphicsUnit unit)
        {
            if (unit == GraphicsUnit.Pixel || unit == GraphicsUnit.World || unit == GraphicsUnit.Display) return src;
            float sx = GpGraphics.UnitScale(unit, image.DpiX), sy = GpGraphics.UnitScale(unit, image.DpiY);
            return new RectangleF(src.X * sx, src.Y * sy, src.Width * sx, src.Height * sy);
        }

        static int DrawImageRect(GpGraphics g, GpImage image, RectangleF dst, RectangleF src, GpImageAttributes attributes)
            => DrawImage(g, image, src, dst.Location, new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom), attributes);

        // DrawImage(image, x, y): at the image's physical size (its resolution relative to the Graphics').
        static SizeF PhysicalSize(GpGraphics g, GpImage image)
        {
            float w = image.Width * g.DpiX / image.DpiX, h = image.Height * g.DpiY / image.DpiY; // device pixels
            var page = g.PageMatrix;
            return new SizeF(w / page.M11, h / page.M22);
        }

        internal static int GdipDrawImage(HandleRef graphics, HandleRef image, float x, float y)
        {
            var g = G(graphics);
            var img = Image(image);
            if (g == null || img == null) return InvalidParameter;
            var size = PhysicalSize(g, img);
            return DrawImageRect(g, img, new RectangleF(x, y, size.Width, size.Height), new RectangleF(0, 0, img.Width, img.Height), null);
        }

        internal static int GdipDrawImageI(HandleRef graphics, HandleRef image, int x, int y) => GdipDrawImage(graphics, image, x, y);

        internal static int GdipDrawImageRect(HandleRef graphics, HandleRef image, float x, float y, float width, float height)
        {
            var img = Image(image);
            if (img == null) return InvalidParameter;
            return DrawImageRect(G(graphics), img, new RectangleF(x, y, width, height), new RectangleF(0, 0, img.Width, img.Height), null);
        }

        internal static int GdipDrawImageRectI(HandleRef graphics, HandleRef image, int x, int y, int width, int height) => GdipDrawImageRect(graphics, image, x, y, width, height);

        internal static int GdipDrawImagePointRect(HandleRef graphics, HandleRef image, float x, float y, float srcx, float srcy, float srcwidth, float srcheight, int srcunit)
        {
            var g = G(graphics);
            var img = Image(image);
            if (g == null || img == null) return InvalidParameter;
            var src = SourcePixels(img, new RectangleF(srcx, srcy, srcwidth, srcheight), (GraphicsUnit)srcunit);
            float w = src.Width * g.DpiX / img.DpiX / g.PageMatrix.M11, h = src.Height * g.DpiY / img.DpiY / g.PageMatrix.M22;
            return DrawImageRect(g, img, new RectangleF(x, y, w, h), src, null);
        }

        internal static int GdipDrawImagePointRectI(HandleRef graphics, HandleRef image, int x, int y, int srcx, int srcy, int srcwidth, int srcheight, int srcunit)
            => GdipDrawImagePointRect(graphics, image, x, y, srcx, srcy, srcwidth, srcheight, srcunit);

        internal static int GdipDrawImageRectRect(HandleRef graphics, HandleRef image, float dstx, float dsty, float dstwidth, float dstheight, float srcx, float srcy, float srcwidth, float srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata)
        {
            var img = Image(image);
            if (img == null) return InvalidParameter;
            var src = SourcePixels(img, new RectangleF(srcx, srcy, srcwidth, srcheight), srcunit);
            return DrawImageRect(G(graphics), img, new RectangleF(dstx, dsty, dstwidth, dstheight), src, Get<GpImageAttributes>(imageAttributes));
        }

        internal static int GdipDrawImageRectRectI(HandleRef graphics, HandleRef image, int dstx, int dsty, int dstwidth, int dstheight, int srcx, int srcy, int srcwidth, int srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata)
            => GdipDrawImageRectRect(graphics, image, dstx, dsty, dstwidth, dstheight, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);

        internal static int GdipDrawImagePoints(HandleRef graphics, HandleRef image, PointF* points, int count)
        {
            var img = Image(image);
            if (img == null || points == null || count != 3) return count != 3 ? NotImplemented : InvalidParameter;
            return DrawImage(G(graphics), img, new RectangleF(0, 0, img.Width, img.Height), points[0], points[1], points[2], null);
        }

        internal static int GdipDrawImagePointsI(HandleRef graphics, HandleRef image, Point* points, int count)
        {
            if (points == null || count != 3) return count != 3 ? NotImplemented : InvalidParameter;
            var f = stackalloc PointF[3];
            for (int i = 0; i < 3; i++) f[i] = points[i];
            return GdipDrawImagePoints(graphics, image, f, 3);
        }

        internal static int GdipDrawImagePointsRect(HandleRef graphics, HandleRef image, PointF* points, int count, float srcx, float srcy, float srcwidth, float srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata)
        {
            var img = Image(image);
            if (img == null || points == null) return InvalidParameter;
            if (count != 3) return NotImplemented;
            var src = SourcePixels(img, new RectangleF(srcx, srcy, srcwidth, srcheight), srcunit);
            return DrawImage(G(graphics), img, src, points[0], points[1], points[2], Get<GpImageAttributes>(imageAttributes));
        }

        internal static int GdipDrawImagePointsRectI(HandleRef graphics, HandleRef image, Point* points, int count, int srcx, int srcy, int srcwidth, int srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata)
        {
            if (points == null) return InvalidParameter;
            if (count != 3) return NotImplemented;
            var f = stackalloc PointF[3];
            for (int i = 0; i < 3; i++) f[i] = points[i];
            return GdipDrawImagePointsRect(graphics, image, f, 3, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);
        }
    }
    }
}
