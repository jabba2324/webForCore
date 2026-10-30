// webForCore: GDI+ image attributes (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    // Colour adjustments per ColorAdjustType; a type without its own settings uses Default's. Applied to the source
    // pixels in GDI+'s order: remap table, colour key, colour matrix, threshold, gamma, output channel.
    internal sealed class GpImageAttributes
    {
        internal sealed class Settings
        {
            public bool NoOp;
            public float[,] Matrix, Gray;
            public ColorMatrixFlag MatrixFlags;
            public float? Threshold, Gamma;
            public int? KeyLow, KeyHigh;
            public ColorChannelFlag? Channel;
            public Dictionary<int, int> Remap;
            public Settings Clone()
            {
                var s = (Settings)MemberwiseClone();
                s.Remap = Remap == null ? null : new Dictionary<int, int>(Remap);
                return s;
            }
            public bool IsEmpty => !NoOp && Matrix == null && Threshold == null && Gamma == null && KeyLow == null && Channel == null && Remap == null;
        }

        public readonly Dictionary<ColorAdjustType, Settings> Types = new Dictionary<ColorAdjustType, Settings>();
        public WrapMode WrapMode = WrapMode.Clamp;
        public int WrapColor;

        public GpImageAttributes Clone()
        {
            var c = new GpImageAttributes { WrapMode = WrapMode, WrapColor = WrapColor };
            foreach (var kv in Types) c.Types[kv.Key] = kv.Value.Clone();
            return c;
        }

        public Settings For(ColorAdjustType type, bool create)
        {
            if (!Types.TryGetValue(type, out var s) && create) Types[type] = s = new Settings();
            return s;
        }

        Settings Effective(ColorAdjustType type)
        {
            if (Types.TryGetValue(type, out var s) && !s.IsEmpty) return s;
            return Types.TryGetValue(ColorAdjustType.Default, out var d) ? d : null;
        }

        public bool Affects(ColorAdjustType type)
        {
            var s = Effective(type);
            return s != null && !s.NoOp && !s.IsEmpty;
        }

        // A copy of the bitmap with the adjustments applied, or the bitmap itself when there are none.
        public SKBitmap Apply(SKBitmap source, ColorAdjustType type)
        {
            var s = Effective(type);
            if (s == null || s.NoOp || s.IsEmpty) return source;
            var result = source.Copy(SKColorType.Bgra8888);
            int w = result.Width, h = result.Height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = result.GetPixel(x, y);
                    int argb = (int)(uint)c;
                    if (s.Remap != null && s.Remap.TryGetValue(argb, out int mapped)) argb = mapped;
                    float a = (argb >> 24 & 255) / 255f, r = (argb >> 16 & 255) / 255f, g = (argb >> 8 & 255) / 255f, b = (argb & 255) / 255f;
                    if (s.KeyLow is int lo && s.KeyHigh is int hi
                        && (argb >> 16 & 255) >= (lo >> 16 & 255) && (argb >> 16 & 255) <= (hi >> 16 & 255)
                        && (argb >> 8 & 255) >= (lo >> 8 & 255) && (argb >> 8 & 255) <= (hi >> 8 & 255)
                        && (argb & 255) >= (lo & 255) && (argb & 255) <= (hi & 255))
                        a = 0;
                    if (s.Matrix != null)
                    {
                        bool gray = r == g && g == b;
                        float[,] m = s.MatrixFlags == ColorMatrixFlag.AltGrays && gray && s.Gray != null ? s.Gray : s.Matrix;
                        if (!(s.MatrixFlags == ColorMatrixFlag.SkipGrays && gray))
                        {
                            float[] v = { r, g, b, a, 1 };
                            float[] o = new float[4];
                            for (int j = 0; j < 4; j++)
                                for (int i = 0; i < 5; i++) o[j] += v[i] * m[i, j];
                            r = o[0]; g = o[1]; b = o[2]; a = o[3];
                        }
                    }
                    if (s.Threshold is float t) { r = r > t ? 1 : 0; g = g > t ? 1 : 0; b = b > t ? 1 : 0; }
                    if (s.Gamma is float gamma && gamma > 0)
                    {
                        r = (float)Math.Pow(Math.Clamp(r, 0, 1), gamma); g = (float)Math.Pow(Math.Clamp(g, 0, 1), gamma); b = (float)Math.Pow(Math.Clamp(b, 0, 1), gamma);
                    }
                    if (s.Channel is ColorChannelFlag channel)
                    {
                        // CMYK channel as a grey level (C = 1 - R, ...).
                        float k = 1 - Math.Max(r, Math.Max(g, b));
                        float level = channel switch
                        {
                            ColorChannelFlag.ColorChannelC => k >= 1 ? 0 : (1 - r - k) / (1 - k),
                            ColorChannelFlag.ColorChannelM => k >= 1 ? 0 : (1 - g - k) / (1 - k),
                            ColorChannelFlag.ColorChannelY => k >= 1 ? 0 : (1 - b - k) / (1 - k),
                            _ => k,
                        };
                        r = g = b = 1 - level;
                    }
                    byte B(float v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
                    result.SetPixel(x, y, new SKColor(B(r), B(g), B(b), B(a)));
                }
            return result;
        }
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        static GpImageAttributes Attributes(HandleRef a) => Get<GpImageAttributes>(a);

        static int SetAttribute(HandleRef imageattr, ColorAdjustType type, Action<GpImageAttributes.Settings> set)
        {
            var a = Attributes(imageattr);
            if (a == null || type < ColorAdjustType.Default || type > ColorAdjustType.Text) return InvalidParameter;
            set(a.For(type, true));
            return Ok;
        }

        internal static int GdipCreateImageAttributes(out IntPtr imageattr) { imageattr = NewHandle(new GpImageAttributes()); return Ok; }
        internal static int GdipCloneImageAttributes(HandleRef imageattr, out IntPtr cloneImageattr) { cloneImageattr = IntPtr.Zero; var a = Attributes(imageattr); if (a == null) return InvalidParameter; cloneImageattr = NewHandle(a.Clone()); return Ok; }
        internal static int GdipDisposeImageAttributes(HandleRef imageattr) => Free(imageattr);

        static float[,] ToArray(ColorMatrix m)
        {
            var a = new float[5, 5];
            for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++) a[i, j] = m[i, j];
            return a;
        }

        internal static int GdipSetImageAttributesColorMatrix(HandleRef imageattr, ColorAdjustType type, bool enableFlag, ColorMatrix? colorMatrix, ColorMatrix? grayMatrix, ColorMatrixFlag flags)
        {
            if (enableFlag && colorMatrix == null) return InvalidParameter;
            if (flags < ColorMatrixFlag.Default || flags > ColorMatrixFlag.AltGrays) return InvalidParameter;
            if (enableFlag && flags == ColorMatrixFlag.AltGrays && grayMatrix == null) return InvalidParameter;
            return SetAttribute(imageattr, type, s =>
            {
                s.Matrix = enableFlag ? ToArray(colorMatrix) : null;
                s.Gray = enableFlag && grayMatrix != null ? ToArray(grayMatrix) : null;
                s.MatrixFlags = flags;
            });
        }

        internal static int GdipSetImageAttributesThreshold(HandleRef imageattr, ColorAdjustType type, bool enableFlag, float threshold) => SetAttribute(imageattr, type, s => s.Threshold = enableFlag ? threshold : null);

        internal static int GdipSetImageAttributesGamma(HandleRef imageattr, ColorAdjustType type, bool enableFlag, float gamma)
        {
            if (enableFlag && gamma <= 0) return InvalidParameter;
            return SetAttribute(imageattr, type, s => s.Gamma = enableFlag ? gamma : null);
        }

        internal static int GdipSetImageAttributesNoOp(HandleRef imageattr, ColorAdjustType type, bool enableFlag) => SetAttribute(imageattr, type, s => s.NoOp = enableFlag);

        internal static int GdipSetImageAttributesColorKeys(HandleRef imageattr, ColorAdjustType type, bool enableFlag, int colorLow, int colorHigh)
            => SetAttribute(imageattr, type, s => { s.KeyLow = enableFlag ? colorLow : null; s.KeyHigh = enableFlag ? colorHigh : null; });

        internal static int GdipSetImageAttributesOutputChannel(HandleRef imageattr, ColorAdjustType type, bool enableFlag, ColorChannelFlag flags)
        {
            if (enableFlag && (flags < ColorChannelFlag.ColorChannelC || flags > ColorChannelFlag.ColorChannelK)) return InvalidParameter;
            return SetAttribute(imageattr, type, s => s.Channel = enableFlag ? flags : null);
        }

        internal static int GdipSetImageAttributesOutputChannelColorProfile(HandleRef imageattr, ColorAdjustType type, bool enableFlag, string colorProfileFilename)
            => Attributes(imageattr) == null ? InvalidParameter : NotImplemented; // ICC profiles: not supported

        internal static int GdipSetImageAttributesRemapTable(HandleRef imageattr, ColorAdjustType type, bool enableFlag, int mapSize, IntPtr map)
        {
            if (enableFlag && (mapSize <= 0 || map == IntPtr.Zero)) return InvalidParameter;
            Dictionary<int, int> table = null;
            if (enableFlag)
            {
                table = new Dictionary<int, int>();
                for (int i = 0; i < mapSize; i++) table[Marshal.ReadInt32(map, i * 8)] = Marshal.ReadInt32(map, i * 8 + 4);
            }
            return SetAttribute(imageattr, type, s => s.Remap = table);
        }

        internal static int GdipSetImageAttributesWrapMode(HandleRef imageattr, int wrapmode, int argb, bool clamp)
        {
            var a = Attributes(imageattr);
            if (a == null) return InvalidParameter;
            a.WrapMode = (WrapMode)wrapmode;
            a.WrapColor = argb;
            return Ok;
        }

        internal static int GdipGetImageAttributesAdjustedPalette(HandleRef imageattr, IntPtr palette, ColorAdjustType type)
        {
            var a = Attributes(imageattr);
            if (a == null || palette == IntPtr.Zero) return InvalidParameter;
            int count = Marshal.ReadInt32(palette, 4);
            if (count <= 0) return InvalidParameter;
            using var bitmap = new SKBitmap(count, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            for (int i = 0; i < count; i++) bitmap.SetPixel(i, 0, new SKColor((uint)Marshal.ReadInt32(palette, 8 + i * 4)));
            var adjusted = a.Apply(bitmap, type);
            for (int i = 0; i < count; i++) Marshal.WriteInt32(palette, 8 + i * 4, (int)(uint)adjusted.GetPixel(i, 0));
            if (adjusted != bitmap) adjusted.Dispose();
            return Ok;
        }
    }
    }
}
