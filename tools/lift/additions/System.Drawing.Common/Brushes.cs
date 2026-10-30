// webForCore: GDI+ brushes, pens, matrices and line caps on SkiaSharp (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    internal abstract class GpBrush
    {
        public abstract BrushType Type { get; }
        public abstract GpBrush Clone();
        // Sets the paint's colour or shader. deviceMatrix: the canvas matrix (world to device) when drawing.
        public abstract void Apply(SKPaint paint, SKMatrix deviceMatrix, Point renderingOrigin);
        protected static SKColor ToColor(int argb) => new SKColor((uint)argb);
    }

    internal sealed class GpSolidBrush : GpBrush
    {
        public int Color;
        public override BrushType Type => BrushType.SolidColor;
        public override GpBrush Clone() => new GpSolidBrush { Color = Color };
        public override void Apply(SKPaint paint, SKMatrix deviceMatrix, Point origin) { paint.Shader = null; paint.Color = ToColor(Color); }
    }

    // 8x8 device-aligned patterns, anchored at the rendering origin.
    internal sealed class GpHatchBrush : GpBrush
    {
        public int Style, Fore, Back;
        public override BrushType Type => BrushType.HatchFill;
        public override GpBrush Clone() => new GpHatchBrush { Style = Style, Fore = Fore, Back = Back };

        public override void Apply(SKPaint paint, SKMatrix deviceMatrix, Point origin)
        {
            var bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
            byte[] rows = Pattern(Style);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    bitmap.SetPixel(x, y, ((rows[y] >> (7 - x)) & 1) != 0 ? ToColor(Fore) : ToColor(Back));
            deviceMatrix.TryInvert(out var inverse);
            var local = SKMatrix.Concat(inverse, SKMatrix.CreateTranslation(origin.X, origin.Y));
            paint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, local);
            paint.Color = SKColors.Black;
        }

        // HatchStyle patterns (rows top to bottom, bit 7 = left).
        static byte[] Pattern(int style) => style switch
        {
            0 => new byte[] { 0xff, 0, 0, 0, 0, 0, 0, 0 },                                     // Horizontal
            1 => new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 },               // Vertical
            2 => new byte[] { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 },               // ForwardDiagonal
            3 => new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80 },               // BackwardDiagonal
            4 => new byte[] { 0xff, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 },               // Cross
            5 => new byte[] { 0x81, 0x42, 0x24, 0x18, 0x18, 0x24, 0x42, 0x81 },               // DiagonalCross
            6 => new byte[] { 0x80, 0, 0, 0, 0x08, 0, 0, 0 },                                  // Percent05
            7 => new byte[] { 0x80, 0, 0x08, 0, 0x80, 0, 0x08, 0 },                            // Percent10
            8 => new byte[] { 0x88, 0, 0x22, 0, 0x88, 0, 0x22, 0 },                            // Percent20
            9 => new byte[] { 0x88, 0x22, 0x88, 0x22, 0x88, 0x22, 0x88, 0x22 },               // Percent25
            10 => new byte[] { 0xaa, 0x44, 0xaa, 0x11, 0xaa, 0x44, 0xaa, 0x11 },              // Percent30
            11 => new byte[] { 0xaa, 0x44, 0xaa, 0x55, 0xaa, 0x44, 0xaa, 0x55 },              // Percent40
            12 => new byte[] { 0xaa, 0x55, 0xaa, 0x55, 0xaa, 0x55, 0xaa, 0x55 },              // Percent50
            13 => new byte[] { 0xee, 0x55, 0xbb, 0x55, 0xee, 0x55, 0xbb, 0x55 },              // Percent60
            14 => new byte[] { 0xee, 0x77, 0xbb, 0xdd, 0xee, 0x77, 0xbb, 0xdd },              // Percent70
            15 => new byte[] { 0xff, 0x77, 0xff, 0xdd, 0xff, 0x77, 0xff, 0xdd },              // Percent75
            16 => new byte[] { 0xff, 0x77, 0xff, 0xff, 0xff, 0x77, 0xff, 0xff },              // Percent80
            17 => new byte[] { 0xff, 0xff, 0xff, 0x7f, 0xff, 0xff, 0xff, 0xf7 },              // Percent90
            18 => new byte[] { 0x88, 0x44, 0x22, 0x11, 0x88, 0x44, 0x22, 0x11 },              // LightDownwardDiagonal
            19 => new byte[] { 0x11, 0x22, 0x44, 0x88, 0x11, 0x22, 0x44, 0x88 },              // LightUpwardDiagonal
            20 => new byte[] { 0xcc, 0x66, 0x33, 0x99, 0xcc, 0x66, 0x33, 0x99 },              // DarkDownwardDiagonal
            21 => new byte[] { 0x33, 0x66, 0xcc, 0x99, 0x33, 0x66, 0xcc, 0x99 },              // DarkUpwardDiagonal
            24 => new byte[] { 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88 },              // LightVertical
            25 => new byte[] { 0xff, 0, 0, 0, 0xff, 0, 0, 0 },                                 // LightHorizontal
            26 => new byte[] { 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa },              // NarrowVertical
            27 => new byte[] { 0xff, 0, 0xff, 0, 0xff, 0, 0xff, 0 },                          // NarrowHorizontal
            28 => new byte[] { 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc },              // DarkVertical
            29 => new byte[] { 0xff, 0xff, 0, 0, 0xff, 0xff, 0, 0 },                          // DarkHorizontal
            32 => new byte[] { 0xff, 0, 0, 0, 0, 0, 0, 0xff },                                 // DashedHorizontal (approximation)
            33 => new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08, 0x08, 0x08, 0x08 },              // DashedVertical (approximation)
            36 => new byte[] { 0x80, 0x80, 0x80, 0xff, 0x08, 0x08, 0x08, 0xff },              // HorizontalBrick
            40 => new byte[] { 0x80, 0x80, 0x41, 0x3e, 0x08, 0x08, 0x14, 0xe3 },              // Weave (approximation)
            43 => new byte[] { 0xff, 0x88, 0x88, 0x88, 0xff, 0x88, 0x88, 0x88 },              // SmallGrid
            46 => new byte[] { 0xff, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 },              // LargeGrid (= Cross)
            47 => new byte[] { 0x88, 0x00, 0x22, 0x00, 0x88, 0x00, 0x22, 0x00 },              // DottedGrid (approximation)
            50 => new byte[] { 0xcc, 0xcc, 0x33, 0x33, 0xcc, 0xcc, 0x33, 0x33 },              // SmallCheckerBoard
            51 => new byte[] { 0xf0, 0xf0, 0xf0, 0xf0, 0x0f, 0x0f, 0x0f, 0x0f },              // LargeCheckerBoard
            52 => new byte[] { 0x81, 0x42, 0x24, 0x18, 0x18, 0x24, 0x42, 0x81 },              // OutlinedDiamond (approximation)
            53 => new byte[] { 0x10, 0x38, 0x7c, 0xfe, 0x7c, 0x38, 0x10, 0x00 },              // SolidDiamond
            _ => new byte[] { 0xaa, 0x55, 0xaa, 0x55, 0xaa, 0x55, 0xaa, 0x55 },               // others: approximated by 50%
        };
    }

    internal abstract class GpGradientBrush : GpBrush
    {
        public GpMatrix Transform = new GpMatrix();
        public WrapMode WrapMode = WrapMode.Tile;
        public float[] BlendFactors = { 1 }, BlendPositions = { 0 };
        public int[] PresetColors;
        public float[] PresetPositions;
        public bool GammaCorrection;

        protected void CopyTo(GpGradientBrush b)
        {
            b.Transform = Transform.Clone();
            b.WrapMode = WrapMode;
            b.BlendFactors = (float[])BlendFactors.Clone();
            b.BlendPositions = (float[])BlendPositions.Clone();
            b.PresetColors = (int[])PresetColors?.Clone();
            b.PresetPositions = (float[])PresetPositions?.Clone();
            b.GammaCorrection = GammaCorrection;
        }

        // Stops from the start colour to the end colour, through the blend or the preset colours.
        protected (SKColor[] Colors, float[] Positions) Stops(int start, int end)
        {
            if (PresetColors != null && PresetColors.Length >= 2)
                return (PresetColors.Select(c => new SKColor((uint)c)).ToArray(), PresetPositions);
            if (BlendFactors.Length <= 1)
                return (new[] { ToColor(start), ToColor(end) }, new[] { 0f, 1f });
            var colors = BlendFactors.Select(f => Lerp(start, end, f)).ToArray();
            return (colors, BlendPositions);
        }

        protected static SKColor Lerp(int a, int b, float t)
        {
            byte L(int shift) => (byte)Math.Round(((a >> shift) & 255) + (((b >> shift) & 255) - ((a >> shift) & 255)) * t);
            return new SKColor(L(16), L(8), L(0), L(24));
        }

        // GDI+'s SetSigmaBellShape: a bell curve sampled at 256 points on each side of the focus (one side when the focus
        // is at an end, 256 points; otherwise 511), from 0 at the ends to `scale` at the focus. Each side is the
        // complement of a normal cumulative distribution, so it starts and ends exactly at 0 and scale.
        public void SetSigmaBlend(float focus, float scale)
        {
            const int side = 256;
            var factors = new List<float>();
            var positions = new List<float>();
            if (focus > 0)
                for (int i = 0; i < side; i++)
                {
                    positions.Add(i == side - 1 ? focus : focus * i / (side - 1));
                    factors.Add(scale * Bell(1 - i / (float)(side - 1)));
                }
            if (focus < 1)
                for (int i = focus > 0 ? 1 : 0; i < side; i++)
                {
                    positions.Add(i == side - 1 ? 1 : focus + (1 - focus) * i / (side - 1));
                    factors.Add(scale * Bell(i / (float)(side - 1)));
                }
            BlendFactors = factors.ToArray();
            BlendPositions = positions.ToArray();
            PresetColors = null;
        }

        // 1 at d = 0 (the focus), 0 at d = 1 (an end).
        static float Bell(float d)
        {
            if (d <= 0) return 1;
            if (d >= 1) return 0;
            const double c = 2.5; // the curve's steepness: sigma = 1 / (2c) of the half-width
            return (float)((1 - Erf((d - 0.5) * 2 * c) / Erf(c)) / 2);
        }

        // Abramowitz and Stegun 7.1.26 (|error| < 1.5e-7).
        static double Erf(double x)
        {
            double t = 1 / (1 + 0.3275911 * Math.Abs(x));
            double y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return x < 0 ? -y : y;
        }

        public void SetLinearBlend(float focus, float scale)
        {
            if (focus == 0) { BlendFactors = new[] { scale, 0f }; BlendPositions = new[] { 0f, 1f }; }
            else if (focus == 1) { BlendFactors = new[] { 0f, scale }; BlendPositions = new[] { 0f, 1f }; }
            else { BlendFactors = new[] { 0f, scale, 0f }; BlendPositions = new[] { 0f, focus, 1f }; }
            PresetColors = null;
        }

        protected SKShaderTileMode TileMode => WrapMode switch
        {
            WrapMode.Tile => SKShaderTileMode.Repeat,
            WrapMode.Clamp => SKShaderTileMode.Clamp,
            _ => SKShaderTileMode.Mirror,
        };
    }

    // GDI+'s model: a horizontal gradient across Rect, from its left edge (Color1) to its right edge (Color2), mapped
    // through Transform. The constructors' angle lives in Transform (as GDI+ reports it): a rotation about the centre
    // of Rect, scaled so the gradient spans the rectangle's corners in that direction. ResetTransform makes it
    // horizontal again.
    internal sealed class GpLinearGradientBrush : GpGradientBrush
    {
        public RectangleF Rect;
        public int Color1, Color2;
        public override BrushType Type => BrushType.LinearGradient;

        public override GpBrush Clone()
        {
            var b = new GpLinearGradientBrush { Rect = Rect, Color1 = Color1, Color2 = Color2 };
            CopyTo(b);
            return b;
        }

        public override void Apply(SKPaint paint, SKMatrix deviceMatrix, Point origin)
        {
            var (colors, positions) = Stops(Color1, Color2);
            paint.Color = SKColors.Black;
            float cy = Rect.Y + Rect.Height / 2;
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(Rect.Left, cy), new SKPoint(Rect.Right, cy), colors, positions, TileMode, Transform.ToSkia());
        }

        // The transform for a gradient at `degrees` across r. With `scalable`, the angle is measured in r scaled to a
        // square (the gradient keeps its slope relative to the rectangle when r is stretched).
        public static GpMatrix InitialTransform(RectangleF r, double degrees, bool scalable)
        {
            double a = degrees * Math.PI / 180;
            if (scalable && r.Width > 0 && r.Height > 0)
                a = Math.Atan2(Math.Sin(a) * r.Height / r.Width, Math.Cos(a));
            double c = Math.Cos(a), s = Math.Sin(a);
            if (Math.Abs(c) < 1e-7) c = 0;
            if (Math.Abs(s) < 1e-7) s = 0;
            double wRatio = (Math.Abs(c) * r.Width + Math.Abs(s) * r.Height) / r.Width;
            double hRatio = (Math.Abs(s) * r.Width + Math.Abs(c) * r.Height) / r.Height;
            float m11 = (float)(c * wRatio), m12 = (float)(s * wRatio), m21 = (float)(-s * hRatio), m22 = (float)(c * hRatio);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            // Rotate and scale about the centre: the centre maps to itself.
            return new GpMatrix(m11, m12, m21, m22, cx - (cx * m11 + cy * m21), cy - (cx * m12 + cy * m22));
        }
    }

    // Colours interpolated from the centre to the boundary points, drawn as a triangle fan with vertex colours.
    internal sealed class GpPathGradientBrush : GpGradientBrush
    {
        public List<PointF> Boundary = new List<PointF>();
        public PointF Center;
        public int CenterColor = -1;
        public int[] SurroundColors = { -1 };
        public PointF FocusScales;
        public override BrushType Type => BrushType.PathGradient;

        public override GpBrush Clone()
        {
            var b = new GpPathGradientBrush { Boundary = new List<PointF>(Boundary), Center = Center, CenterColor = CenterColor, SurroundColors = (int[])SurroundColors.Clone(), FocusScales = FocusScales };
            CopyTo(b);
            return b;
        }

        public RectangleF Bounds
        {
            get
            {
                if (Boundary.Count == 0) return RectangleF.Empty;
                float l = Boundary.Min(p => p.X), t = Boundary.Min(p => p.Y), r = Boundary.Max(p => p.X), b = Boundary.Max(p => p.Y);
                return new RectangleF(l, t, r - l, b - t);
            }
        }

        public override void Apply(SKPaint paint, SKMatrix deviceMatrix, Point origin)
        {
            var bounds = Bounds;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            int n = Boundary.Count;
            var positions = new List<SKPoint>();
            var colors = new List<SKColor>();
            for (int i = 0; i < n; i++)
            {
                var a = Boundary[i];
                var b = Boundary[(i + 1) % n];
                positions.Add(new SKPoint(Center.X, Center.Y)); colors.Add(new SKColor((uint)CenterColor));
                positions.Add(new SKPoint(a.X, a.Y)); colors.Add(new SKColor((uint)SurroundColors[Math.Min(i, SurroundColors.Length - 1)]));
                positions.Add(new SKPoint(b.X, b.Y)); colors.Add(new SKColor((uint)SurroundColors[Math.Min((i + 1) % n, SurroundColors.Length - 1)]));
            }
            using (var p = new SKPaint { IsAntialias = true })
                canvas.DrawVertices(SKVertexMode.Triangles, positions.ToArray(), null, colors.ToArray(), SKBlendMode.Dst, null, p);
            using var picture = recorder.EndRecording();
            paint.Color = SKColors.Black;
            paint.Shader = SKShader.CreatePicture(picture, SKShaderTileMode.Decal, SKShaderTileMode.Decal, Transform.ToSkia(), picture.CullRect);
        }
    }

    internal sealed class GpTextureBrush : GpBrush
    {
        public GpImage Image;
        public WrapMode WrapMode = WrapMode.Tile;
        public GpMatrix Transform = new GpMatrix();
        public override BrushType Type => BrushType.TextureFill;
        public override GpBrush Clone() => new GpTextureBrush { Image = Image.Clone(), WrapMode = WrapMode, Transform = Transform.Clone() };

        public override void Apply(SKPaint paint, SKMatrix deviceMatrix, Point origin)
        {
            var (x, y) = WrapMode switch
            {
                WrapMode.Tile => (SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
                WrapMode.TileFlipX => (SKShaderTileMode.Mirror, SKShaderTileMode.Repeat),
                WrapMode.TileFlipY => (SKShaderTileMode.Repeat, SKShaderTileMode.Mirror),
                WrapMode.TileFlipXY => (SKShaderTileMode.Mirror, SKShaderTileMode.Mirror),
                _ => (SKShaderTileMode.Decal, SKShaderTileMode.Decal),
            };
            paint.Color = SKColors.Black;
            paint.Shader = SKShader.CreateBitmap(Image.Pixels, x, y, Transform.ToSkia());
        }
    }

    internal sealed class GpCustomLineCap
    {
        public GpPath Fill, Stroke;
        public LineCap BaseCap, StartCap, EndCap;
        public LineJoin Join;
        public float BaseInset, WidthScale = 1;
        public bool IsArrow;
        public float ArrowHeight, ArrowWidth, MiddleInset;
        public bool Filled = true;
        public GpCustomLineCap Clone() => (GpCustomLineCap)MemberwiseClone();
    }

    internal sealed class GpPen
    {
        public int Color = unchecked((int)0xff000000);
        public GpBrush Brush;
        public float Width = 1;
        public GraphicsUnit Unit = GraphicsUnit.World;
        public LineCap StartCap, EndCap;
        public DashCap DashCap;
        public LineJoin LineJoin;
        public float MiterLimit = 10;
        public PenAlignment Alignment;
        public DashStyle DashStyle;
        public float[] DashPattern;
        public float DashOffset;
        public GpMatrix Transform = new GpMatrix();
        public float[] Compound = Array.Empty<float>();
        public GpCustomLineCap CustomStart, CustomEnd;

        public GpPen Clone()
        {
            var p = (GpPen)MemberwiseClone();
            p.Brush = Brush?.Clone();
            p.DashPattern = (float[])DashPattern?.Clone();
            p.Transform = Transform.Clone();
            p.Compound = (float[])Compound.Clone();
            return p;
        }

        public float[] Pattern => DashStyle switch
        {
            DashStyle.Dash => new[] { 3f, 1f },
            DashStyle.Dot => new[] { 1f, 1f },
            DashStyle.DashDot => new[] { 3f, 1f, 1f, 1f },
            DashStyle.DashDotDot => new[] { 3f, 1f, 1f, 1f, 1f, 1f },
            DashStyle.Custom => DashPattern,
            _ => null,
        };

        // A stroke paint; width already converted to world units by the caller.
        public void Apply(SKPaint paint, float width, SKMatrix deviceMatrix, Point origin)
        {
            if (Brush != null) Brush.Apply(paint, deviceMatrix, origin);
            else { paint.Shader = null; paint.Color = new SKColor((uint)Color); }
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = width;
            paint.StrokeMiter = MiterLimit;
            paint.StrokeJoin = LineJoin switch { LineJoin.Bevel => SKStrokeJoin.Bevel, LineJoin.Round => SKStrokeJoin.Round, _ => SKStrokeJoin.Miter };
            var cap = DashStyle != DashStyle.Solid ? DashCap switch { DashCap.Round => LineCap.Round, DashCap.Triangle => LineCap.Triangle, _ => LineCap.Flat } : StartCap;
            paint.StrokeCap = cap switch { LineCap.Round or LineCap.RoundAnchor => SKStrokeCap.Round, LineCap.Square or LineCap.SquareAnchor => SKStrokeCap.Square, _ => SKStrokeCap.Butt };
            var pattern = Pattern;
            if (pattern != null && pattern.Length >= 2)
            {
                float unit = Math.Max(width, 1);
                var intervals = pattern.Select(v => Math.Max(v * unit, 0.01f)).ToArray();
                if (intervals.Length % 2 == 1) intervals = intervals.Concat(intervals).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(intervals, DashOffset * unit);
            }
            else
            {
                paint.PathEffect = null;
            }
        }
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        // Brushes

        internal static int GdipCreateSolidFill(int color, out IntPtr brush) { brush = NewHandle(new GpSolidBrush { Color = color }); return Ok; }

        internal static int GdipSetSolidFillColor(HandleRef brush, int color)
        {
            if (Get<GpBrush>(brush) is not GpSolidBrush b) return InvalidParameter;
            b.Color = color;
            return Ok;
        }

        internal static int GdipGetSolidFillColor(HandleRef brush, out int color)
        {
            color = 0;
            if (Get<GpBrush>(brush) is not GpSolidBrush b) return InvalidParameter;
            color = b.Color;
            return Ok;
        }

        internal static int GdipCloneBrush(HandleRef brush, out IntPtr clonebrush)
        {
            clonebrush = IntPtr.Zero;
            var b = Get<GpBrush>(brush);
            if (b == null) return InvalidParameter;
            clonebrush = NewHandle(b.Clone());
            return Ok;
        }

        internal static int GdipDeleteBrush(HandleRef brush) => Free(brush);

        internal static int GdipGetBrushType(IntPtr brush, out BrushType type)
        {
            var b = Get<GpBrush>(brush);
            type = b?.Type ?? BrushType.SolidColor;
            return b == null ? InvalidParameter : Ok;
        }

        internal static int GdipCreateHatchBrush(int hatchstyle, int forecol, int backcol, out IntPtr brush)
        {
            brush = IntPtr.Zero;
            if (hatchstyle < 0 || hatchstyle > 52) return InvalidParameter;
            brush = NewHandle(new GpHatchBrush { Style = hatchstyle, Fore = forecol, Back = backcol });
            return Ok;
        }

        internal static int GdipGetHatchStyle(HandleRef brush, out int hatchstyle) { var b = Get<GpBrush>(brush) as GpHatchBrush; hatchstyle = b?.Style ?? 0; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetHatchForegroundColor(HandleRef brush, out int forecol) { var b = Get<GpBrush>(brush) as GpHatchBrush; forecol = b?.Fore ?? 0; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetHatchBackgroundColor(HandleRef brush, out int backcol) { var b = Get<GpBrush>(brush) as GpHatchBrush; backcol = b?.Back ?? 0; return b == null ? InvalidParameter : Ok; }

        // Linear gradients

        static int NewLinear(RectangleF rect, double angle, bool scalable, int c1, int c2, WrapMode wrap, out IntPtr brush)
        {
            brush = IntPtr.Zero;
            if (wrap == WrapMode.Clamp) return InvalidParameter;
            var transform = GpLinearGradientBrush.InitialTransform(rect, angle, scalable);
            brush = NewHandle(new GpLinearGradientBrush { Rect = rect, Color1 = c1, Color2 = c2, WrapMode = wrap, Transform = transform });
            return Ok;
        }

        internal static int GdipCreateLineBrush(ref PointF point1, ref PointF point2, int color1, int color2, WrapMode wrapMode, out IntPtr lineGradient)
        {
            lineGradient = IntPtr.Zero;
            if (point1 == point2) return OutOfMemory; // GDI+
            var rect = RectangleF.FromLTRB(Math.Min(point1.X, point2.X), Math.Min(point1.Y, point2.Y), Math.Max(point1.X, point2.X), Math.Max(point1.Y, point2.Y));
            if (rect.Width == 0) { rect.X -= Math.Abs(point2.Y - point1.Y) / 2; rect.Width = Math.Abs(point2.Y - point1.Y); }
            if (rect.Height == 0) { rect.Y -= Math.Abs(point2.X - point1.X) / 2; rect.Height = Math.Abs(point2.X - point1.X); }
            double angle = Math.Atan2(point2.Y - point1.Y, point2.X - point1.X) * 180 / Math.PI;
            return NewLinear(rect, angle, false, color1, color2, wrapMode, out lineGradient);
        }

        internal static int GdipCreateLineBrushI(ref Point point1, ref Point point2, int color1, int color2, WrapMode wrapMode, out IntPtr lineGradient)
        {
            PointF a = point1, b = point2;
            return GdipCreateLineBrush(ref a, ref b, color1, color2, wrapMode, out lineGradient);
        }

        internal static int GdipCreateLineBrushFromRect(ref RectangleF rect, int color1, int color2, LinearGradientMode lineGradientMode, WrapMode wrapMode, out IntPtr lineGradient)
        {
            lineGradient = IntPtr.Zero;
            if (rect.Width == 0 || rect.Height == 0) return OutOfMemory;
            float angle = lineGradientMode switch { LinearGradientMode.Vertical => 90, LinearGradientMode.ForwardDiagonal => 45, LinearGradientMode.BackwardDiagonal => 135, _ => 0 };
            return NewLinear(rect, angle, true, color1, color2, wrapMode, out lineGradient);
        }

        internal static int GdipCreateLineBrushFromRectI(ref Rectangle rect, int color1, int color2, LinearGradientMode lineGradientMode, WrapMode wrapMode, out IntPtr lineGradient)
        {
            RectangleF r = rect;
            return GdipCreateLineBrushFromRect(ref r, color1, color2, lineGradientMode, wrapMode, out lineGradient);
        }

        internal static int GdipCreateLineBrushFromRectWithAngle(ref RectangleF rect, int color1, int color2, float angle, bool isAngleScaleable, WrapMode wrapMode, out IntPtr lineGradient)
        {
            lineGradient = IntPtr.Zero;
            if (rect.Width == 0 || rect.Height == 0) return OutOfMemory;
            return NewLinear(rect, angle, isAngleScaleable, color1, color2, wrapMode, out lineGradient);
        }

        internal static int GdipCreateLineBrushFromRectWithAngleI(ref Rectangle rect, int color1, int color2, float angle, bool isAngleScaleable, WrapMode wrapMode, out IntPtr lineGradient)
        {
            RectangleF r = rect;
            return GdipCreateLineBrushFromRectWithAngle(ref r, color1, color2, angle, isAngleScaleable, wrapMode, out lineGradient);
        }

        static GpLinearGradientBrush Linear(HandleRef brush) => Get<GpBrush>(brush) as GpLinearGradientBrush;
        static GpGradientBrush Gradient(HandleRef brush) => Get<GpBrush>(brush) as GpGradientBrush;

        internal static int GdipSetLineColors(HandleRef brush, int color1, int color2) { var b = Linear(brush); if (b == null) return InvalidParameter; b.Color1 = color1; b.Color2 = color2; return Ok; }
        internal static int GdipGetLineColors(HandleRef brush, int[] colors) { var b = Linear(brush); if (b == null || colors == null || colors.Length < 2) return InvalidParameter; colors[0] = b.Color1; colors[1] = b.Color2; return Ok; }
        internal static int GdipGetLineRect(HandleRef brush, out RectangleF gprectf) { var b = Linear(brush); gprectf = b?.Rect ?? default; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetLineGammaCorrection(HandleRef brush, out bool useGammaCorrection) { var b = Linear(brush); useGammaCorrection = b?.GammaCorrection ?? false; return b == null ? InvalidParameter : Ok; }
        internal static int GdipSetLineGammaCorrection(HandleRef brush, bool useGammaCorrection) { var b = Linear(brush); if (b == null) return InvalidParameter; b.GammaCorrection = useGammaCorrection; return Ok; }
        internal static int GdipSetLineSigmaBlend(HandleRef brush, float focus, float scale) { var b = Linear(brush); if (b == null || focus < 0 || focus > 1 || scale < 0 || scale > 1) return InvalidParameter; b.SetSigmaBlend(focus, scale); return Ok; }
        internal static int GdipSetLineLinearBlend(HandleRef brush, float focus, float scale) { var b = Linear(brush); if (b == null || focus < 0 || focus > 1 || scale < 0 || scale > 1) return InvalidParameter; b.SetLinearBlend(focus, scale); return Ok; }
        internal static int GdipGetLineBlendCount(HandleRef brush, out int count) { var b = Linear(brush); count = b?.BlendFactors.Length ?? 0; return b == null ? InvalidParameter : Ok; }

        internal static int GdipGetLineBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count)
        {
            var b = Linear(brush);
            if (b == null || count < b.BlendFactors.Length) return InvalidParameter;
            Marshal.Copy(b.BlendFactors, 0, blend, b.BlendFactors.Length);
            Marshal.Copy(b.BlendPositions, 0, positions, b.BlendPositions.Length);
            return Ok;
        }

        internal static int GdipSetLineBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count)
        {
            var b = Linear(brush);
            if (b == null || count <= 0) return InvalidParameter;
            b.BlendFactors = new float[count];
            b.BlendPositions = new float[count];
            Marshal.Copy(blend, b.BlendFactors, 0, count);
            Marshal.Copy(positions, b.BlendPositions, 0, count);
            b.PresetColors = null;
            return Ok;
        }

        internal static int GdipGetLinePresetBlendCount(HandleRef brush, out int count) { var b = Linear(brush); count = b?.PresetColors?.Length ?? 0; return b == null ? InvalidParameter : Ok; }

        internal static int GdipGetLinePresetBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count)
        {
            var b = Linear(brush);
            if (b == null || b.PresetColors == null || count < b.PresetColors.Length) return InvalidParameter;
            Marshal.Copy(b.PresetColors, 0, blend, b.PresetColors.Length);
            Marshal.Copy(b.PresetPositions, 0, positions, b.PresetPositions.Length);
            return Ok;
        }

        internal static int GdipSetLinePresetBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count)
        {
            var b = Linear(brush);
            if (b == null || count < 2) return InvalidParameter;
            b.PresetColors = new int[count];
            b.PresetPositions = new float[count];
            Marshal.Copy(blend, b.PresetColors, 0, count);
            Marshal.Copy(positions, b.PresetPositions, 0, count);
            return Ok;
        }

        internal static int GdipSetLineWrapMode(HandleRef brush, int wrapMode) { var b = Linear(brush); if (b == null || wrapMode == (int)WrapMode.Clamp) return InvalidParameter; b.WrapMode = (WrapMode)wrapMode; return Ok; }
        internal static int GdipGetLineWrapMode(HandleRef brush, out int wrapMode) { var b = Linear(brush); wrapMode = (int)(b?.WrapMode ?? 0); return b == null ? InvalidParameter : Ok; }
        internal static int GdipResetLineTransform(HandleRef brush) => WithMatrix(Linear(brush)?.Transform, m => m.Reset());
        internal static int GdipMultiplyLineTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => Multiply(Linear(brush)?.Transform, matrix, order);
        internal static int GdipGetLineTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(Linear(brush)?.Transform, matrix, true);
        internal static int GdipSetLineTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(Linear(brush)?.Transform, matrix, false);
        internal static int GdipTranslateLineTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Linear(brush)?.Transform, m => m.Translate(dx, dy, order));
        internal static int GdipScaleLineTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Linear(brush)?.Transform, m => m.Scale(sx, sy, order));
        internal static int GdipRotateLineTransform(HandleRef brush, float angle, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Linear(brush)?.Transform, m => m.Rotate(angle, order));

        static bool BadOrder(MatrixOrder order) => order != MatrixOrder.Prepend && order != MatrixOrder.Append;

        static int WithMatrix(GpMatrix m, Action<GpMatrix> action)
        {
            if (m == null) return InvalidParameter;
            action(m);
            return Ok;
        }

        static int Multiply(GpMatrix target, HandleRef matrix, MatrixOrder order)
        {
            var m = Get<GpMatrix>(matrix);
            if (target == null || m == null) return InvalidParameter;
            // Brushes and pens (GDI+): a non-invertible matrix is rejected and an invalid order means Append.
            if (!m.IsInvertible) return InvalidParameter;
            target.Multiply(m, BadOrder(order) ? MatrixOrder.Append : order);
            return Ok;
        }

        // get: target -> matrix; set: matrix -> target.
        static int CopyMatrix(GpMatrix target, HandleRef matrix, bool get)
        {
            var m = Get<GpMatrix>(matrix);
            if (target == null || m == null) return InvalidParameter;
            if (!get && !m.IsInvertible) return InvalidParameter; // a brush or pen transform must be invertible (GDI+)
            if (get) m.Set(target); else target.Set(m);
            return Ok;
        }

        // Path gradients

        internal static int GdipCreatePathGradient(PointF* points, int count, WrapMode wrapMode, out IntPtr brush)
        {
            brush = IntPtr.Zero;
            if (points == null || count < 2) return OutOfMemory;
            var b = new GpPathGradientBrush { WrapMode = wrapMode, SurroundColors = new[] { -1 } };
            for (int i = 0; i < count; i++) b.Boundary.Add(points[i]);
            b.Center = new PointF(b.Boundary.Average(p => p.X), b.Boundary.Average(p => p.Y));
            b.CenterColor = unchecked((int)0xff000000);
            brush = NewHandle(b);
            return Ok;
        }

        internal static int GdipCreatePathGradientI(Point* points, int count, WrapMode wrapMode, out IntPtr brush)
        {
            var f = new PointF[Math.Max(count, 0)];
            for (int i = 0; i < count; i++) f[i] = points[i];
            fixed (PointF* p = f) return GdipCreatePathGradient(p, count, wrapMode, out brush);
        }

        internal static int GdipCreatePathGradientFromPath(HandleRef path, out IntPtr brush)
        {
            brush = IntPtr.Zero;
            var p = Get<GpPath>(path);
            if (p == null || p.Count < 2) return OutOfMemory;
            using var sk = p.ToSkia();
            var flat = Flatten(sk);
            var b = new GpPathGradientBrush { WrapMode = WrapMode.Clamp, SurroundColors = new[] { -1 } };
            b.Boundary.AddRange(flat);
            b.Center = new PointF(flat.Average(q => q.X), flat.Average(q => q.Y));
            b.CenterColor = unchecked((int)0xff000000);
            brush = NewHandle(b);
            return Ok;
        }

        static List<PointF> Flatten(SKPath path)
        {
            var result = new List<PointF>();
            using var measure = new SKPathMeasure(path, false);
            do
            {
                float length = measure.Length;
                int steps = Math.Max(8, (int)(length / 2));
                // An open figure keeps its end point (a closed one would repeat its start).
                int last = measure.IsClosed ? steps - 1 : steps;
                for (int i = 0; i <= last; i++)
                    if (measure.GetPosition(length * i / steps, out var pt)) result.Add(new PointF(pt.X, pt.Y));
            } while (measure.NextContour());
            return result;
        }

        static GpPathGradientBrush PathGradient(HandleRef brush) => Get<GpBrush>(brush) as GpPathGradientBrush;

        internal static int GdipGetPathGradientCenterColor(HandleRef brush, out int color) { var b = PathGradient(brush); color = b?.CenterColor ?? 0; return b == null ? InvalidParameter : Ok; }
        internal static int GdipSetPathGradientCenterColor(HandleRef brush, int color) { var b = PathGradient(brush); if (b == null) return InvalidParameter; b.CenterColor = color; return Ok; }

        internal static int GdipGetPathGradientSurroundColorsWithCount(HandleRef brush, int[] color, ref int count)
        {
            var b = PathGradient(brush);
            if (b == null || color == null || count < b.SurroundColors.Length) return InvalidParameter;
            Array.Copy(b.SurroundColors, color, b.SurroundColors.Length);
            count = b.SurroundColors.Length;
            return Ok;
        }

        internal static int GdipSetPathGradientSurroundColorsWithCount(HandleRef brush, int[] argb, ref int count)
        {
            var b = PathGradient(brush);
            if (b == null || argb == null || count <= 0 || count > b.Boundary.Count) return InvalidParameter;
            // GDI+ keeps one colour when they are all the same.
            b.SurroundColors = argb.Take(count).Distinct().Count() == 1 ? new[] { argb[0] } : argb.Take(count).ToArray();
            return Ok;
        }

        internal static int GdipGetPathGradientCenterPoint(HandleRef brush, out PointF point) { var b = PathGradient(brush); point = b?.Center ?? default; return b == null ? InvalidParameter : Ok; }
        internal static int GdipSetPathGradientCenterPoint(HandleRef brush, ref PointF point) { var b = PathGradient(brush); if (b == null) return InvalidParameter; b.Center = point; return Ok; }
        internal static int GdipGetPathGradientRect(HandleRef brush, out RectangleF gprectf) { var b = PathGradient(brush); gprectf = b?.Bounds ?? default; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetPathGradientPointCount(HandleRef brush, out int count) { var b = PathGradient(brush); count = b?.Boundary.Count ?? 0; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetPathGradientSurroundColorCount(HandleRef brush, out int count) { var b = PathGradient(brush); count = b?.SurroundColors.Length ?? 0; return b == null ? InvalidParameter : Ok; }
        internal static int GdipGetPathGradientBlendCount(HandleRef brush, out int count) { var b = PathGradient(brush); count = b?.BlendFactors.Length ?? 0; return b == null ? InvalidParameter : Ok; }

        internal static int GdipGetPathGradientBlend(HandleRef brush, float[] blend, float[] positions, int count)
        {
            var b = PathGradient(brush);
            if (b == null || blend == null || positions == null || count < b.BlendFactors.Length) return InvalidParameter;
            Array.Copy(b.BlendFactors, blend, b.BlendFactors.Length);
            Array.Copy(b.BlendPositions, positions, b.BlendPositions.Length);
            return Ok;
        }

        internal static int GdipSetPathGradientBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count)
        {
            var b = PathGradient(brush);
            if (b == null || count <= 0) return InvalidParameter;
            b.BlendFactors = new float[count];
            b.BlendPositions = new float[count];
            Marshal.Copy(blend, b.BlendFactors, 0, count);
            Marshal.Copy(positions, b.BlendPositions, 0, count);
            b.PresetColors = null;
            return Ok;
        }

        internal static int GdipGetPathGradientPresetBlendCount(HandleRef brush, out int count) { var b = PathGradient(brush); count = b?.PresetColors?.Length ?? 0; return b == null ? InvalidParameter : Ok; }

        internal static int GdipGetPathGradientPresetBlend(HandleRef brush, int[] blend, float[] positions, int count)
        {
            var b = PathGradient(brush);
            if (b == null || b.PresetColors == null || count < b.PresetColors.Length) return InvalidParameter;
            Array.Copy(b.PresetColors, blend, b.PresetColors.Length);
            Array.Copy(b.PresetPositions, positions, b.PresetPositions.Length);
            return Ok;
        }

        internal static int GdipSetPathGradientPresetBlend(HandleRef brush, int[] blend, float[] positions, int count)
        {
            var b = PathGradient(brush);
            if (b == null || count < 2) return InvalidParameter;
            b.PresetColors = blend.Take(count).ToArray();
            b.PresetPositions = positions.Take(count).ToArray();
            return Ok;
        }

        internal static int GdipSetPathGradientSigmaBlend(HandleRef brush, float focus, float scale) { var b = PathGradient(brush); if (b == null || focus < 0 || focus > 1 || scale < 0 || scale > 1) return InvalidParameter; b.SetSigmaBlend(focus, scale); return Ok; }
        internal static int GdipSetPathGradientLinearBlend(HandleRef brush, float focus, float scale) { var b = PathGradient(brush); if (b == null || focus < 0 || focus > 1 || scale < 0 || scale > 1) return InvalidParameter; b.SetLinearBlend(focus, scale); return Ok; }
        internal static int GdipSetPathGradientWrapMode(HandleRef brush, int wrapmode) { var b = PathGradient(brush); if (b == null) return InvalidParameter; b.WrapMode = (WrapMode)wrapmode; return Ok; }
        internal static int GdipGetPathGradientWrapMode(HandleRef brush, out int wrapmode) { var b = PathGradient(brush); wrapmode = (int)(b?.WrapMode ?? 0); return b == null ? InvalidParameter : Ok; }
        internal static int GdipSetPathGradientTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(PathGradient(brush)?.Transform, matrix, false);
        internal static int GdipGetPathGradientTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(PathGradient(brush)?.Transform, matrix, true);
        internal static int GdipResetPathGradientTransform(HandleRef brush) => WithMatrix(PathGradient(brush)?.Transform, m => m.Reset());
        internal static int GdipMultiplyPathGradientTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => Multiply(PathGradient(brush)?.Transform, matrix, order);
        internal static int GdipTranslatePathGradientTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(PathGradient(brush)?.Transform, m => m.Translate(dx, dy, order));
        internal static int GdipScalePathGradientTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(PathGradient(brush)?.Transform, m => m.Scale(sx, sy, order));
        internal static int GdipRotatePathGradientTransform(HandleRef brush, float angle, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(PathGradient(brush)?.Transform, m => m.Rotate(angle, order));

        internal static int GdipGetPathGradientFocusScales(HandleRef brush, float[] xScale, float[] yScale)
        {
            var b = PathGradient(brush);
            if (b == null || xScale == null || yScale == null) return InvalidParameter;
            xScale[0] = b.FocusScales.X;
            yScale[0] = b.FocusScales.Y;
            return Ok;
        }

        internal static int GdipSetPathGradientFocusScales(HandleRef brush, float xScale, float yScale) { var b = PathGradient(brush); if (b == null) return InvalidParameter; b.FocusScales = new PointF(xScale, yScale); return Ok; }

        // Textures

        static int NewTexture(GpImage image, WrapMode wrap, RectangleF? area, GpImageAttributes attributes, out IntPtr texture)
        {
            texture = IntPtr.Zero;
            if (image == null) return InvalidParameter;
            var source = image.Clone();
            if (area is RectangleF r && (r.X != 0 || r.Y != 0 || r.Width != image.Width || r.Height != image.Height))
            {
                var rect = Rectangle.Round(r);
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > image.Width || rect.Bottom > image.Height) return OutOfMemory;
                var sub = new SKBitmap();
                source.Pixels.ExtractSubset(sub, new SKRectI(rect.X, rect.Y, rect.Right, rect.Bottom));
                var copy = sub.Copy();
                sub.Dispose();
                source.Pixels.Dispose();
                source.Pixels = copy;
            }
            if (attributes != null)
            {
                var filtered = attributes.Apply(source.Pixels, System.Drawing.Imaging.ColorAdjustType.Brush);
                if (filtered != source.Pixels)
                {
                    source.Pixels.Dispose();
                    source.Pixels = filtered;
                }
                wrap = attributes.WrapMode;
            }
            texture = NewHandle(new GpTextureBrush { Image = source, WrapMode = wrap });
            return Ok;
        }

        internal static int GdipCreateTexture(HandleRef bitmap, int wrapmode, out IntPtr texture) => NewTexture(Image(bitmap), (WrapMode)wrapmode, null, null, out texture);
        internal static int GdipCreateTexture2(HandleRef bitmap, int wrapmode, float x, float y, float width, float height, out IntPtr texture) => NewTexture(Image(bitmap), (WrapMode)wrapmode, new RectangleF(x, y, width, height), null, out texture);
        internal static int GdipCreateTexture2I(HandleRef bitmap, int wrapmode, int x, int y, int width, int height, out IntPtr texture) => NewTexture(Image(bitmap), (WrapMode)wrapmode, new RectangleF(x, y, width, height), null, out texture);
        internal static int GdipCreateTextureIA(HandleRef bitmap, HandleRef imageAttrib, float x, float y, float width, float height, out IntPtr texture) => NewTexture(Image(bitmap), WrapMode.Tile, new RectangleF(x, y, width, height), Get<GpImageAttributes>(imageAttrib), out texture);
        internal static int GdipCreateTextureIAI(HandleRef bitmap, HandleRef imageAttrib, int x, int y, int width, int height, out IntPtr texture) => NewTexture(Image(bitmap), WrapMode.Tile, new RectangleF(x, y, width, height), Get<GpImageAttributes>(imageAttrib), out texture);

        static GpTextureBrush Texture(HandleRef brush) => Get<GpBrush>(brush) as GpTextureBrush;
        internal static int GdipSetTextureTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(Texture(brush)?.Transform, matrix, false);
        internal static int GdipGetTextureTransform(HandleRef brush, HandleRef matrix) => CopyMatrix(Texture(brush)?.Transform, matrix, true);
        internal static int GdipResetTextureTransform(HandleRef brush) => WithMatrix(Texture(brush)?.Transform, m => m.Reset());
        internal static int GdipMultiplyTextureTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => Multiply(Texture(brush)?.Transform, matrix, order);
        internal static int GdipTranslateTextureTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Texture(brush)?.Transform, m => m.Translate(dx, dy, order));
        internal static int GdipScaleTextureTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Texture(brush)?.Transform, m => m.Scale(sx, sy, order));
        internal static int GdipRotateTextureTransform(HandleRef brush, float angle, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Texture(brush)?.Transform, m => m.Rotate(angle, order));
        internal static int GdipSetTextureWrapMode(HandleRef brush, int wrapMode) { var b = Texture(brush); if (b == null) return InvalidParameter; b.WrapMode = (WrapMode)wrapMode; return Ok; }
        internal static int GdipGetTextureWrapMode(HandleRef brush, out int wrapMode) { var b = Texture(brush); wrapMode = (int)(b?.WrapMode ?? 0); return b == null ? InvalidParameter : Ok; }

        internal static int GdipGetTextureImage(HandleRef brush, out IntPtr image)
        {
            image = IntPtr.Zero;
            var b = Texture(brush);
            if (b == null) return InvalidParameter;
            image = NewHandle(b.Image.Clone());
            return Ok;
        }

        // Pens

        internal static int GdipCreatePen1(int argb, float width, int unit, out IntPtr pen)
        {
            pen = IntPtr.Zero;
            if (unit < 0 || unit > 6 || unit == (int)GraphicsUnit.Display) return InvalidParameter;
            pen = NewHandle(new GpPen { Color = argb, Width = width, Unit = (GraphicsUnit)unit });
            return Ok;
        }

        internal static int GdipCreatePen2(HandleRef brush, float width, int unit, out IntPtr pen)
        {
            pen = IntPtr.Zero;
            var b = Get<GpBrush>(brush);
            if (b == null || unit < 0 || unit > 6 || unit == (int)GraphicsUnit.Display) return InvalidParameter;
            var p = new GpPen { Brush = b.Clone(), Width = width, Unit = (GraphicsUnit)unit };
            if (b is GpSolidBrush s) { p.Color = s.Color; p.Brush = null; }
            pen = NewHandle(p);
            return Ok;
        }

        static GpPen Pen(HandleRef pen) => Get<GpPen>(pen);

        internal static int GdipClonePen(HandleRef pen, out IntPtr clonepen) { clonepen = IntPtr.Zero; var p = Pen(pen); if (p == null) return InvalidParameter; clonepen = NewHandle(p.Clone()); return Ok; }
        internal static int GdipDeletePen(HandleRef Pen) => Free(Pen);
        internal static int GdipSetPenMode(HandleRef pen, PenAlignment penAlign) { var p = Pen(pen); if (p == null) return InvalidParameter; p.Alignment = penAlign; return Ok; }
        internal static int GdipGetPenMode(HandleRef pen, out PenAlignment penAlign) { var p = Pen(pen); penAlign = p?.Alignment ?? 0; return p == null ? InvalidParameter : Ok; }
        internal static int GdipSetPenWidth(HandleRef pen, float width) { var p = Pen(pen); if (p == null) return InvalidParameter; p.Width = width; return Ok; }
        internal static int GdipGetPenWidth(HandleRef pen, float[] width) { var p = Pen(pen); if (p == null) return InvalidParameter; width[0] = p.Width; return Ok; }
        internal static int GdipSetPenLineCap197819(HandleRef pen, int startCap, int endCap, int dashCap) { var p = Pen(pen); if (p == null) return InvalidParameter; p.StartCap = (LineCap)startCap; p.EndCap = (LineCap)endCap; p.DashCap = ValidDashCap(dashCap) ? (DashCap)dashCap : DashCap.Flat; return Ok; }
        static bool ValidDashCap(int cap) => cap == (int)DashCap.Flat || cap == (int)DashCap.Round || cap == (int)DashCap.Triangle;
        internal static int GdipSetPenStartCap(HandleRef pen, int startCap) { var p = Pen(pen); if (p == null) return InvalidParameter; p.StartCap = (LineCap)startCap; p.CustomStart = null; return Ok; }
        internal static int GdipSetPenEndCap(HandleRef pen, int endCap) { var p = Pen(pen); if (p == null) return InvalidParameter; p.EndCap = (LineCap)endCap; p.CustomEnd = null; return Ok; }
        internal static int GdipGetPenStartCap(HandleRef pen, out int startCap) { var p = Pen(pen); startCap = (int)(p?.StartCap ?? 0); return p == null ? InvalidParameter : Ok; }
        internal static int GdipGetPenEndCap(HandleRef pen, out int endCap) { var p = Pen(pen); endCap = (int)(p?.EndCap ?? 0); return p == null ? InvalidParameter : Ok; }
        internal static int GdipGetPenDashCap197819(HandleRef pen, out int dashCap) { var p = Pen(pen); dashCap = (int)(p?.DashCap ?? 0); return p == null ? InvalidParameter : Ok; }
        internal static int GdipSetPenDashCap197819(HandleRef pen, int dashCap) { var p = Pen(pen); if (p == null) return InvalidParameter; p.DashCap = (DashCap)dashCap; return Ok; }
        internal static int GdipSetPenLineJoin(HandleRef pen, int lineJoin) { var p = Pen(pen); if (p == null) return InvalidParameter; p.LineJoin = (LineJoin)lineJoin; return Ok; }
        internal static int GdipGetPenLineJoin(HandleRef pen, out int lineJoin) { var p = Pen(pen); lineJoin = (int)(p?.LineJoin ?? 0); return p == null ? InvalidParameter : Ok; }

        internal static int GdipSetPenCustomStartCap(HandleRef pen, HandleRef customCap) { var p = Pen(pen); var c = Get<GpCustomLineCap>(customCap); if (p == null || c == null) return InvalidParameter; p.CustomStart = c.Clone(); p.StartCap = LineCap.Custom; return Ok; }
        internal static int GdipSetPenCustomEndCap(HandleRef pen, HandleRef customCap) { var p = Pen(pen); var c = Get<GpCustomLineCap>(customCap); if (p == null || c == null) return InvalidParameter; p.CustomEnd = c.Clone(); p.EndCap = LineCap.Custom; return Ok; }

        internal static int GdipGetPenCustomStartCap(HandleRef pen, out IntPtr customCap)
        {
            customCap = IntPtr.Zero;
            var p = Pen(pen);
            if (p == null) return InvalidParameter;
            if (p.CustomStart != null) customCap = NewHandle(p.CustomStart.Clone());
            return Ok;
        }

        internal static int GdipGetPenCustomEndCap(HandleRef pen, out IntPtr customCap)
        {
            customCap = IntPtr.Zero;
            var p = Pen(pen);
            if (p == null) return InvalidParameter;
            if (p.CustomEnd != null) customCap = NewHandle(p.CustomEnd.Clone());
            return Ok;
        }

        internal static int GdipSetPenMiterLimit(HandleRef pen, float miterLimit) { var p = Pen(pen); if (p == null) return InvalidParameter; p.MiterLimit = Math.Max(1, miterLimit); return Ok; }
        internal static int GdipGetPenMiterLimit(HandleRef pen, float[] miterLimit) { var p = Pen(pen); if (p == null) return InvalidParameter; miterLimit[0] = p.MiterLimit; return Ok; }

        internal static int GdipSetPenTransform(HandleRef pen, HandleRef matrix)
        {
            var p = Pen(pen);
            var m = Get<GpMatrix>(matrix);
            if (p == null || m == null) return InvalidParameter;
            if (!m.IsInvertible) return InvalidParameter;
            p.Transform.Set(m);
            return Ok;
        }

        internal static int GdipGetPenTransform(HandleRef pen, HandleRef matrix) => CopyMatrix(Pen(pen)?.Transform, matrix, true);
        internal static int GdipResetPenTransform(HandleRef brush) => WithMatrix(Pen(brush)?.Transform, m => m.Reset());
        internal static int GdipMultiplyPenTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => Multiply(Pen(brush)?.Transform, matrix, order);
        internal static int GdipTranslatePenTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Pen(brush)?.Transform, m => m.Translate(dx, dy, order));
        internal static int GdipScalePenTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Pen(brush)?.Transform, m => m.Scale(sx, sy, order));
        internal static int GdipRotatePenTransform(HandleRef brush, float angle, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Pen(brush)?.Transform, m => m.Rotate(angle, order));

        internal static int GdipSetPenColor(HandleRef pen, int argb) { var p = Pen(pen); if (p == null) return InvalidParameter; p.Color = argb; p.Brush = null; return Ok; }

        internal static int GdipGetPenColor(HandleRef pen, out int argb)
        {
            var p = Pen(pen);
            argb = p == null ? 0 : p.Brush is GpSolidBrush s ? s.Color : p.Color;
            if (p == null) return InvalidParameter;
            return p.Brush == null || p.Brush is GpSolidBrush ? Ok : InvalidParameter;
        }

        internal static int GdipSetPenBrushFill(HandleRef pen, HandleRef brush)
        {
            var p = Pen(pen);
            var b = Get<GpBrush>(brush);
            if (p == null || b == null) return InvalidParameter;
            if (b is GpSolidBrush s) { p.Color = s.Color; p.Brush = null; }
            else p.Brush = b.Clone();
            return Ok;
        }

        internal static int GdipGetPenBrushFill(HandleRef pen, out IntPtr brush)
        {
            brush = IntPtr.Zero;
            var p = Pen(pen);
            if (p == null) return InvalidParameter;
            brush = NewHandle(p.Brush?.Clone() ?? new GpSolidBrush { Color = p.Color });
            return Ok;
        }

        internal static int GdipGetPenFillType(HandleRef pen, out int pentype)
        {
            var p = Pen(pen);
            pentype = p == null ? 0 : p.Brush == null ? (int)PenType.SolidColor : (int)p.Brush.Type;
            return p == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetPenDashStyle(HandleRef pen, out int dashstyle) { var p = Pen(pen); dashstyle = (int)(p?.DashStyle ?? 0); return p == null ? InvalidParameter : Ok; }
        internal static int GdipSetPenDashStyle(HandleRef pen, int dashstyle)
        {
            var p = Pen(pen);
            if (p == null || dashstyle < 0 || dashstyle > 5) return InvalidParameter;
            // Custom keeps the pattern of the style it replaces (GDI+).
            if ((DashStyle)dashstyle == DashStyle.Custom && p.DashStyle != DashStyle.Custom && p.Pattern != null) p.DashPattern = p.Pattern;
            p.DashStyle = (DashStyle)dashstyle;
            return Ok;
        }

        internal static int GdipSetPenDashArray(HandleRef pen, HandleRef memorydash, int count)
        {
            var p = Pen(pen);
            if (p == null || count <= 0 || memorydash.Handle == IntPtr.Zero) return InvalidParameter;
            var dashes = new float[count];
            Marshal.Copy(memorydash.Handle, dashes, 0, count);
            if (dashes.Any(d => d <= 0)) return InvalidParameter;
            p.DashPattern = dashes;
            p.DashStyle = DashStyle.Custom;
            return Ok;
        }

        internal static int GdipGetPenDashOffset(HandleRef pen, float[] dashoffset) { var p = Pen(pen); if (p == null) return InvalidParameter; dashoffset[0] = p.DashOffset; return Ok; }
        internal static int GdipSetPenDashOffset(HandleRef pen, float dashoffset) { var p = Pen(pen); if (p == null) return InvalidParameter; p.DashOffset = dashoffset; return Ok; }
        internal static int GdipGetPenDashCount(HandleRef pen, out int dashcount) { var p = Pen(pen); dashcount = p?.Pattern?.Length ?? 0; return p == null ? InvalidParameter : Ok; }

        internal static int GdipGetPenDashArray(HandleRef pen, float[] memorydash, int count)
        {
            var p = Pen(pen);
            var pattern = p?.Pattern;
            if (p == null || pattern == null || memorydash == null || count < pattern.Length) return InvalidParameter;
            Array.Copy(pattern, memorydash, pattern.Length);
            return Ok;
        }

        internal static int GdipGetPenCompoundCount(HandleRef pen, out int count) { var p = Pen(pen); count = p?.Compound.Length ?? 0; return p == null ? InvalidParameter : Ok; }

        internal static int GdipSetPenCompoundArray(HandleRef pen, float[] array, int count)
        {
            var p = Pen(pen);
            if (p == null || array == null || count < 2 || count % 2 != 0) return InvalidParameter;
            for (int i = 0; i < count; i++)
                if (array[i] < 0 || array[i] > 1 || (i > 0 && array[i] < array[i - 1])) return InvalidParameter;
            p.Compound = array.Take(count).ToArray();
            return Ok;
        }

        internal static int GdipGetPenCompoundArray(HandleRef pen, float[] array, int count)
        {
            var p = Pen(pen);
            if (p == null || array == null || count < p.Compound.Length) return InvalidParameter;
            Array.Copy(p.Compound, array, p.Compound.Length);
            return Ok;
        }

        // Custom line caps (stored; drawn as their base cap)

        internal static int GdipCreateCustomLineCap(HandleRef fillpath, HandleRef strokepath, LineCap baseCap, float baseInset, out IntPtr customCap)
        {
            customCap = IntPtr.Zero;
            var fill = Get<GpPath>(fillpath);
            var stroke = Get<GpPath>(strokepath);
            if (fill == null && stroke == null) return InvalidParameter;
            // A base cap that is not a plain cap (an anchor, Custom, or out of range) becomes Flat (GDI+).
            customCap = NewHandle(new GpCustomLineCap { Fill = fill?.Clone(), Stroke = stroke?.Clone(), BaseCap = PlainCap(baseCap) ? baseCap : LineCap.Flat, BaseInset = baseInset });
            return Ok;
        }

        internal static int GdipCreateAdjustableArrowCap(float height, float width, bool isFilled, out IntPtr adjustableArrowCap)
        {
            adjustableArrowCap = NewHandle(new GpCustomLineCap { IsArrow = true, ArrowHeight = height, ArrowWidth = width, Filled = isFilled, BaseCap = LineCap.Triangle });
            return Ok;
        }

        static GpCustomLineCap Cap(HandleRef cap) => Get<GpCustomLineCap>(cap);
        static int CapSet(HandleRef cap, Action<GpCustomLineCap> set) { var c = Cap(cap); if (c == null) return InvalidParameter; set(c); return Ok; }

        internal static int GdipDeleteCustomLineCap(IntPtr customCap) => Free(customCap);
        internal static int GdipDeleteCustomLineCap(HandleRef customCap) => Free(customCap);
        internal static int GdipCloneCustomLineCap(HandleRef customCap, out IntPtr clonedCap) { clonedCap = IntPtr.Zero; var c = Cap(customCap); if (c == null) return InvalidParameter; clonedCap = NewHandle(c.Clone()); return Ok; }
        internal static int GdipGetCustomLineCapType(IntPtr customCap, out CustomLineCapType capType) { var c = Get<GpCustomLineCap>(customCap); capType = c?.IsArrow == true ? CustomLineCapType.AdjustableArrowCap : CustomLineCapType.Default; return c == null ? InvalidParameter : Ok; }
        // Flat, Square, Round and Triangle: the caps a custom cap can be based on or stroked with.
        static bool PlainCap(LineCap cap) => cap >= LineCap.Flat && cap <= LineCap.Triangle;

        internal static int GdipSetCustomLineCapStrokeCaps(HandleRef customCap, LineCap startCap, LineCap endCap)
            => !PlainCap(startCap) || !PlainCap(endCap) ? InvalidParameter : CapSet(customCap, c => { c.StartCap = startCap; c.EndCap = endCap; });
        internal static int GdipGetCustomLineCapStrokeCaps(HandleRef customCap, out LineCap startCap, out LineCap endCap) { var c = Cap(customCap); startCap = c?.StartCap ?? 0; endCap = c?.EndCap ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetCustomLineCapStrokeJoin(HandleRef customCap, LineJoin lineJoin) => CapSet(customCap, c => c.Join = lineJoin);
        internal static int GdipGetCustomLineCapStrokeJoin(HandleRef customCap, out LineJoin lineJoin) { var c = Cap(customCap); lineJoin = c?.Join ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetCustomLineCapBaseCap(HandleRef customCap, LineCap baseCap) => !PlainCap(baseCap) ? InvalidParameter : CapSet(customCap, c => c.BaseCap = baseCap);
        internal static int GdipGetCustomLineCapBaseCap(HandleRef customCap, out LineCap baseCap) { var c = Cap(customCap); baseCap = c?.BaseCap ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetCustomLineCapBaseInset(HandleRef customCap, float inset) => CapSet(customCap, c => c.BaseInset = inset);
        internal static int GdipGetCustomLineCapBaseInset(HandleRef customCap, out float inset) { var c = Cap(customCap); inset = c?.BaseInset ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetCustomLineCapWidthScale(HandleRef customCap, float widthScale) => CapSet(customCap, c => c.WidthScale = widthScale);
        internal static int GdipGetCustomLineCapWidthScale(HandleRef customCap, out float widthScale) { var c = Cap(customCap); widthScale = c?.WidthScale ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipGetAdjustableArrowCapHeight(HandleRef adjustableArrowCap, out float height) { var c = Cap(adjustableArrowCap); height = c?.ArrowHeight ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetAdjustableArrowCapHeight(HandleRef adjustableArrowCap, float height) => CapSet(adjustableArrowCap, c => c.ArrowHeight = height);
        internal static int GdipGetAdjustableArrowCapWidth(HandleRef adjustableArrowCap, out float width) { var c = Cap(adjustableArrowCap); width = c?.ArrowWidth ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetAdjustableArrowCapWidth(HandleRef adjustableArrowCap, float width) => CapSet(adjustableArrowCap, c => c.ArrowWidth = width);
        internal static int GdipGetAdjustableArrowCapMiddleInset(HandleRef adjustableArrowCap, out float middleInset) { var c = Cap(adjustableArrowCap); middleInset = c?.MiddleInset ?? 0; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetAdjustableArrowCapMiddleInset(HandleRef adjustableArrowCap, float middleInset) => CapSet(adjustableArrowCap, c => c.MiddleInset = middleInset);
        internal static int GdipGetAdjustableArrowCapFillState(HandleRef adjustableArrowCap, out bool fillState) { var c = Cap(adjustableArrowCap); fillState = c?.Filled ?? false; return c == null ? InvalidParameter : Ok; }
        internal static int GdipSetAdjustableArrowCapFillState(HandleRef adjustableArrowCap, bool fillState) => CapSet(adjustableArrowCap, c => c.Filled = fillState);

        // Matrices

        internal static int GdipCreateMatrix(out IntPtr matrix) { matrix = NewHandle(new GpMatrix()); return Ok; }
        internal static int GdipCreateMatrix2(float m11, float m12, float m21, float m22, float dx, float dy, out IntPtr matrix) { matrix = NewHandle(new GpMatrix(m11, m12, m21, m22, dx, dy)); return Ok; }

        internal static int GdipCreateMatrix3(ref RectangleF rect, PointF* dstplg, out IntPtr matrix)
        {
            matrix = IntPtr.Zero;
            if (dstplg == null) return InvalidParameter;
            if (rect.Width == 0 || rect.Height == 0) return OutOfMemory;
            // Maps rect's top-left, top-right and bottom-left to the three points.
            float sx = rect.Width, sy = rect.Height;
            var m11 = (dstplg[1].X - dstplg[0].X) / sx; var m12 = (dstplg[1].Y - dstplg[0].Y) / sx;
            var m21 = (dstplg[2].X - dstplg[0].X) / sy; var m22 = (dstplg[2].Y - dstplg[0].Y) / sy;
            var m = new GpMatrix(m11, m12, m21, m22, dstplg[0].X - rect.X * m11 - rect.Y * m21, dstplg[0].Y - rect.X * m12 - rect.Y * m22);
            matrix = NewHandle(m);
            return Ok;
        }

        internal static int GdipCreateMatrix3I(ref Rectangle rect, Point* dstplg, out IntPtr matrix)
        {
            RectangleF r = rect;
            var pts = stackalloc PointF[3];
            for (int i = 0; i < 3; i++) pts[i] = dstplg[i];
            return GdipCreateMatrix3(ref r, pts, out matrix);
        }

        static GpMatrix Matrix(HandleRef m) => Get<GpMatrix>(m);
        internal static int GdipCloneMatrix(HandleRef matrix, out IntPtr cloneMatrix) { cloneMatrix = IntPtr.Zero; var m = Matrix(matrix); if (m == null) return InvalidParameter; cloneMatrix = NewHandle(m.Clone()); return Ok; }
        internal static int GdipDeleteMatrix(HandleRef matrix) => Free(matrix);
        internal static int GdipSetMatrixElements(HandleRef matrix, float m11, float m12, float m21, float m22, float dx, float dy) => WithMatrix(Matrix(matrix), m => m.Set(new GpMatrix(m11, m12, m21, m22, dx, dy)));
        internal static int GdipMultiplyMatrix(HandleRef matrix, HandleRef matrix2, MatrixOrder order)
        {
            var target = Matrix(matrix);
            var m = Get<GpMatrix>(matrix2);
            if (target == null || m == null || BadOrder(order)) return InvalidParameter;
            target.Multiply(m, order);
            return Ok;
        }
        internal static int GdipTranslateMatrix(HandleRef matrix, float offsetX, float offsetY, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Matrix(matrix), m => m.Translate(offsetX, offsetY, order));
        internal static int GdipScaleMatrix(HandleRef matrix, float scaleX, float scaleY, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Matrix(matrix), m => m.Scale(scaleX, scaleY, order));
        internal static int GdipRotateMatrix(HandleRef matrix, float angle, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Matrix(matrix), m => m.Rotate(angle, order));
        internal static int GdipShearMatrix(HandleRef matrix, float shearX, float shearY, MatrixOrder order) => BadOrder(order) ? InvalidParameter : WithMatrix(Matrix(matrix), m => m.Shear(shearX, shearY, order));
        internal static int GdipInvertMatrix(HandleRef matrix) { var m = Matrix(matrix); if (m == null) return InvalidParameter; return m.Invert() ? Ok : InvalidParameter; }

        internal static int GdipTransformMatrixPoints(HandleRef matrix, PointF* pts, int count) { var m = Matrix(matrix); if (m == null || pts == null) return InvalidParameter; for (int i = 0; i < count; i++) pts[i] = m.Transform(pts[i]); return Ok; }
        internal static int GdipTransformMatrixPointsI(HandleRef matrix, Point* pts, int count) { var m = Matrix(matrix); if (m == null || pts == null) return InvalidParameter; for (int i = 0; i < count; i++) pts[i] = Point.Round(m.Transform(pts[i])); return Ok; }
        internal static int GdipVectorTransformMatrixPoints(HandleRef matrix, PointF* pts, int count) { var m = Matrix(matrix); if (m == null || pts == null) return InvalidParameter; for (int i = 0; i < count; i++) pts[i] = m.TransformVector(pts[i]); return Ok; }
        internal static int GdipVectorTransformMatrixPointsI(HandleRef matrix, Point* pts, int count) { var m = Matrix(matrix); if (m == null || pts == null) return InvalidParameter; for (int i = 0; i < count; i++) pts[i] = Point.Round(m.TransformVector(pts[i])); return Ok; }
    }
    }
}
