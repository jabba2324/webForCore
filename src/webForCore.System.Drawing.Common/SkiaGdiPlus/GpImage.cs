// webForCore: GDI+ images on SkiaSharp (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    // A GDI+ bitmap. The pixels of the active frame are a premultiplied BGRA SKBitmap (what Skia draws into); the GDI+
    // pixel format the image reports (Format24bppRgb for a JPEG, Format8bppIndexed for a GIF, ...) is kept apart and the
    // pixels are converted to it at the edges: LockBits, GetPixel, saving. Drawing therefore never depends on the
    // reported format (libgdiplus corrupted its heap drawing on 24bpp bitmaps).
    internal sealed class GpImage : IDisposable
    {
        public SKBitmap Pixels;
        public PixelFormat Format;
        public Guid RawFormat;
        public float DpiX = 96, DpiY = 96;
        public int[] Palette;          // ARGB entries, for indexed formats
        public int PaletteFlags;
        public List<GpProperty> Properties = new List<GpProperty>();
        public int Flags;              // ImageFlags
        // Frames (animated GIF, multi-page TIFF): the decoder that can produce them, or decoded frames.
        public Guid FrameDimension;
        public int FrameCount = 1;
        public int ActiveFrame;
        public Func<int, SKBitmap> FrameDecoder;
        public SKBitmap[] FrameCache;
        // LockBits state
        public IntPtr LockedBuffer;
        public bool LockedOwnBuffer;
        public Rectangle LockedRect;
        public ImageLockMode LockedMode;
        public PixelFormat LockedFormat;
        public int LockedStride;

        public int Width => Pixels.Width;
        public int Height => Pixels.Height;

        public GpImage(SKBitmap pixels, PixelFormat format, Guid rawFormat)
        {
            Pixels = pixels;
            Format = format;
            RawFormat = rawFormat;
            Flags = DefaultFlags(format);
            if (IsIndexed(format) && Palette == null) Palette = DefaultPalette(format);
        }

        public static GpImage Create(int width, int height, PixelFormat format)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            bitmap.Erase(HasAlpha(format) ? SKColors.Transparent : SKColors.Black);
            return new GpImage(bitmap, format, ImageFormat.MemoryBmp.Guid);
        }

        public GpImage Clone()
        {
            var clone = (GpImage)MemberwiseClone();
            clone.Pixels = Pixels.Copy();
            clone.Palette = (int[])Palette?.Clone();
            clone.Properties = Properties.ConvertAll(p => p.Clone());
            clone.FrameCache = null;
            clone.LockedBuffer = IntPtr.Zero;
            return clone;
        }

        public void Dispose()
        {
            if (LockedOwnBuffer && LockedBuffer != IntPtr.Zero) Marshal.FreeHGlobal(LockedBuffer);
            LockedBuffer = IntPtr.Zero;
            Pixels?.Dispose();
            if (FrameCache != null) foreach (var f in FrameCache) if (f != Pixels) f?.Dispose();
        }

        public void SelectFrame(int index)
        {
            if (FrameDecoder == null || index == ActiveFrame) return;
            FrameCache ??= new SKBitmap[FrameCount];
            FrameCache[ActiveFrame] ??= Pixels;
            Pixels = FrameCache[index] ??= FrameDecoder(index);
            ActiveFrame = index;
        }

        // PixelFormat helpers (gdipluspixelformats.h)
        public static int BitsPerPixel(PixelFormat format) => ((int)format >> 8) & 0xff;
        public static bool IsIndexed(PixelFormat format) => (format & PixelFormat.Indexed) != 0;
        public static bool HasAlpha(PixelFormat format) => (format & PixelFormat.Alpha) != 0;
        public static bool IsPremultiplied(PixelFormat format) => (format & PixelFormat.PAlpha) != 0;
        public static int Stride(int width, PixelFormat format) => ((width * BitsPerPixel(format) + 31) / 32) * 4;

        public static bool IsValidFormat(PixelFormat format) => format switch
        {
            PixelFormat.Format1bppIndexed or PixelFormat.Format4bppIndexed or PixelFormat.Format8bppIndexed
                or PixelFormat.Format16bppGrayScale or PixelFormat.Format16bppRgb555 or PixelFormat.Format16bppRgb565
                or PixelFormat.Format16bppArgb1555 or PixelFormat.Format24bppRgb or PixelFormat.Format32bppRgb
                or PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb or PixelFormat.Format48bppRgb
                or PixelFormat.Format64bppArgb or PixelFormat.Format64bppPArgb => true,
            _ => false,
        };

        static int DefaultFlags(PixelFormat format)
        {
            int flags = (int)ImageFlags.ColorSpaceRgb;
            if (HasAlpha(format)) flags |= (int)ImageFlags.HasAlpha;
            return flags;
        }

        // GDI+'s default palettes: 1bpp black/white, 4bpp the 16 VGA colours, 8bpp the halftone palette.
        public static int[] DefaultPalette(PixelFormat format)
        {
            if (format == PixelFormat.Format1bppIndexed) return new[] { unchecked((int)0xff000000), -1 };
            var vga = new uint[] { 0xff000000, 0xff800000, 0xff008000, 0xff808000, 0xff000080, 0xff800080, 0xff008080, 0xff808080,
                0xffc0c0c0, 0xffff0000, 0xff00ff00, 0xffffff00, 0xff0000ff, 0xffff00ff, 0xff00ffff, 0xffffffff };
            if (format == PixelFormat.Format4bppIndexed) return Array.ConvertAll(vga, v => unchecked((int)v));
            var palette = new int[256];
            for (int i = 0; i < 16; i++) palette[i] = unchecked((int)vga[i]);
            // 216-colour web-safe cube, then transparent black, as GDI+'s halftone palette.
            int n = 40;
            for (int r = 0; r < 6; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 6; b++)
                palette[n++] = unchecked((int)(0xff000000u | (uint)(r * 51) << 16 | (uint)(g * 51) << 8 | (uint)(b * 51)));
            return palette;
        }

        // ARGB (unpremultiplied) of a pixel of the active frame.
        public int GetArgb(int x, int y)
        {
            var c = Pixels.GetPixel(x, y); // SKBitmap.GetPixel returns the unpremultiplied colour
            int argb = (c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;
            return HasAlpha(Format) ? argb : argb | unchecked((int)0xff000000);
        }

        public void SetArgb(int x, int y, int argb)
        {
            uint a = (uint)argb >> 24;
            if (!HasAlpha(Format)) a = 255;
            Pixels.SetPixel(x, y, new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)a));
        }

        // Copies a rectangle of the active frame into a buffer in a GDI+ pixel format (LockBits, saving).
        public unsafe void ReadPixels(Rectangle rect, PixelFormat format, byte* dest, int stride)
        {
            var span = Pixels.GetPixelSpan();
            int rowBytes = Pixels.RowBytes;
            int bpp = BitsPerPixel(format);
            for (int y = 0; y < rect.Height; y++)
            {
                byte* row = dest + (long)y * stride;
                if (IsIndexed(format)) new Span<byte>(row, Math.Abs(stride)).Clear();
                for (int x = 0; x < rect.Width; x++)
                {
                    int o = (rect.Y + y) * rowBytes + (rect.X + x) * 4;
                    uint b = span[o], g = span[o + 1], r = span[o + 2], a = span[o + 3];
                    if (!IsPremultiplied(format) && a != 0 && a != 255)
                    {
                        b = Math.Min(255u, (b * 255 + a / 2) / a); g = Math.Min(255u, (g * 255 + a / 2) / a); r = Math.Min(255u, (r * 255 + a / 2) / a);
                    }
                    switch (format)
                    {
                        case PixelFormat.Format32bppArgb:
                        case PixelFormat.Format32bppPArgb:
                            row[x * 4] = (byte)b; row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)r; row[x * 4 + 3] = (byte)a; break;
                        case PixelFormat.Format32bppRgb:
                            row[x * 4] = (byte)b; row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)r; row[x * 4 + 3] = 255; break;
                        case PixelFormat.Format24bppRgb:
                            row[x * 3] = (byte)b; row[x * 3 + 1] = (byte)g; row[x * 3 + 2] = (byte)r; break;
                        case PixelFormat.Format16bppRgb565:
                            *(ushort*)(row + x * 2) = (ushort)((r >> 3) << 11 | (g >> 2) << 5 | b >> 3); break;
                        case PixelFormat.Format16bppRgb555:
                            *(ushort*)(row + x * 2) = (ushort)((r >> 3) << 10 | (g >> 3) << 5 | b >> 3); break;
                        case PixelFormat.Format16bppArgb1555:
                            *(ushort*)(row + x * 2) = (ushort)((a >= 128 ? 0x8000 : 0) | (r >> 3) << 10 | (g >> 3) << 5 | b >> 3); break;
                        case PixelFormat.Format16bppGrayScale:
                            *(ushort*)(row + x * 2) = (ushort)((r * 299 + g * 587 + b * 114) / 1000 * 257); break;
                        case PixelFormat.Format48bppRgb:
                            ((ushort*)(row + x * 6))[0] = (ushort)(b * 8192 / 255); ((ushort*)(row + x * 6))[1] = (ushort)(g * 8192 / 255);
                            ((ushort*)(row + x * 6))[2] = (ushort)(r * 8192 / 255); break;
                        case PixelFormat.Format64bppArgb:
                        case PixelFormat.Format64bppPArgb:
                            var p = (ushort*)(row + x * 8);
                            p[0] = (ushort)(b * 8192 / 255); p[1] = (ushort)(g * 8192 / 255); p[2] = (ushort)(r * 8192 / 255); p[3] = (ushort)(a * 8192 / 255); break;
                        default: // indexed
                            int index = NearestIndex((int)(a << 24 | r << 16 | g << 8 | b));
                            if (bpp == 8) row[x] = (byte)index;
                            else if (bpp == 4) row[x / 2] |= (byte)(x % 2 == 0 ? index << 4 : index);
                            else if (index != 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                            break;
                    }
                }
            }
        }

        // Writes a buffer in a GDI+ pixel format into a rectangle of the active frame (UnlockBits, creating from Scan0).
        public unsafe void WritePixels(Rectangle rect, PixelFormat format, byte* src, int stride)
        {
            var span = Pixels.GetPixelSpan();
            int rowBytes = Pixels.RowBytes;
            int bpp = BitsPerPixel(format);
            bool alpha = HasAlpha(Format);
            fixed (byte* pixels = span)
            {
                for (int y = 0; y < rect.Height; y++)
                {
                    byte* row = src + (long)y * stride;
                    for (int x = 0; x < rect.Width; x++)
                    {
                        uint b, g, r, a = 255;
                        switch (format)
                        {
                            case PixelFormat.Format32bppArgb:
                            case PixelFormat.Format32bppPArgb:
                                b = row[x * 4]; g = row[x * 4 + 1]; r = row[x * 4 + 2]; a = row[x * 4 + 3]; break;
                            case PixelFormat.Format32bppRgb:
                                b = row[x * 4]; g = row[x * 4 + 1]; r = row[x * 4 + 2]; break;
                            case PixelFormat.Format24bppRgb:
                                b = row[x * 3]; g = row[x * 3 + 1]; r = row[x * 3 + 2]; break;
                            case PixelFormat.Format16bppRgb565:
                            {
                                uint v = *(ushort*)(row + x * 2);
                                r = (v >> 11 & 31) * 255 / 31; g = (v >> 5 & 63) * 255 / 63; b = (v & 31) * 255 / 31; break;
                            }
                            case PixelFormat.Format16bppRgb555:
                            case PixelFormat.Format16bppArgb1555:
                            {
                                uint v = *(ushort*)(row + x * 2);
                                r = (v >> 10 & 31) * 255 / 31; g = (v >> 5 & 31) * 255 / 31; b = (v & 31) * 255 / 31;
                                if (format == PixelFormat.Format16bppArgb1555) a = (v & 0x8000) != 0 ? 255u : 0u;
                                break;
                            }
                            case PixelFormat.Format16bppGrayScale:
                                r = g = b = (uint)(*(ushort*)(row + x * 2) / 257); break;
                            case PixelFormat.Format48bppRgb:
                                b = Math.Min(255u, ((ushort*)(row + x * 6))[0] * 255u / 8192); g = Math.Min(255u, ((ushort*)(row + x * 6))[1] * 255u / 8192);
                                r = Math.Min(255u, ((ushort*)(row + x * 6))[2] * 255u / 8192); break;
                            case PixelFormat.Format64bppArgb:
                            case PixelFormat.Format64bppPArgb:
                            {
                                var p = (ushort*)(row + x * 8);
                                b = Math.Min(255u, p[0] * 255u / 8192); g = Math.Min(255u, p[1] * 255u / 8192); r = Math.Min(255u, p[2] * 255u / 8192); a = Math.Min(255u, p[3] * 255u / 8192);
                                break;
                            }
                            default:
                            {
                                int index = bpp == 8 ? row[x] : bpp == 4 ? (x % 2 == 0 ? row[x / 2] >> 4 : row[x / 2] & 15) : (row[x / 8] >> (7 - x % 8)) & 1;
                                uint argb = Palette != null && index < Palette.Length ? (uint)Palette[index] : 0;
                                a = argb >> 24; r = argb >> 16 & 255; g = argb >> 8 & 255; b = argb & 255; break;
                            }
                        }
                        if (!alpha && !IsIndexed(Format)) a = 255;
                        if (!IsPremultiplied(format) && a != 255)
                        {
                            r = r * a / 255; g = g * a / 255; b = b * a / 255;
                        }
                        byte* o = pixels + (rect.Y + y) * rowBytes + (rect.X + x) * 4;
                        o[0] = (byte)b; o[1] = (byte)g; o[2] = (byte)r; o[3] = (byte)a;
                    }
                }
            }
            Pixels.NotifyPixelsChanged();
        }

        public int NearestIndex(int argb)
        {
            var palette = Palette ?? DefaultPalette(Format);
            int best = 0, bestDistance = int.MaxValue;
            int a = (argb >> 24) & 255, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            for (int i = 0; i < palette.Length; i++)
            {
                int p = palette[i];
                int da = ((p >> 24) & 255) - a, dr = ((p >> 16) & 255) - r, dg = ((p >> 8) & 255) - g, db = (p & 255) - b;
                int d = da * da + dr * dr + dg * dg + db * db;
                if (d < bestDistance) { best = i; bestDistance = d; if (d == 0) break; }
            }
            return best;
        }
    }

    // A GDI+ property item (EXIF/TIFF tag, GIF frame delay, ...).
    internal sealed class GpProperty
    {
        public int Id;
        public short Type;
        public byte[] Value;
        public GpProperty Clone() => new GpProperty { Id = Id, Type = Type, Value = (byte[])Value?.Clone() };
    }
}
