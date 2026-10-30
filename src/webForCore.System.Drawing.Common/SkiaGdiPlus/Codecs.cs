// webForCore: GDI+'s image codecs on SkiaSharp (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using SkiaSharp;

namespace System.Drawing
{
    // GDI+'s built-in codecs. Skia decodes BMP, GIF, ICO, JPEG and PNG and encodes JPEG and PNG; this adds what GDI+ has
    // and Skia does not: the GDI+ pixel format a decoder reports, palettes, EXIF property items, resolution, GIF frame
    // delays, and the BMP, GIF and TIFF encoders. Formats GDI+ cannot read (WebP, AVIF, ...) are rejected as GDI+ does.
    // TIFF is read here (Tiff.Read). Not implemented: EMF/WMF, JPEG-compressed or tiled TIFF.
    internal static class Codecs
    {
        internal sealed class CodecInfo
        {
            public Guid Clsid, Format;
            public string Name, Description, Extension, Mime;
            public int Flags;
            public byte[][] Signatures;
            public byte[][] Masks; // null: every signature byte is significant
        }

        const int Encoder = 1, Decoder = 2, SupportBitmap = 4, SupportVector = 8, Builtin = 0x10000;
        static Guid Clsid(int n) => new Guid(0x557cf400 + n, 0x1a04, 0x11d3, 0x9a, 0x73, 0x00, 0x00, 0xf8, 0x1e, 0xf3, 0x2e);

        // GDI+'s order: BMP, JPEG, GIF, EMF, WMF, TIFF, PNG, ICO.
        public static readonly CodecInfo[] All =
        {
            new CodecInfo { Clsid = Clsid(0), Format = ImageFormat.Bmp.Guid, Name = "Built-in BMP Codec", Description = "BMP", Extension = "*.BMP;*.DIB;*.RLE", Mime = "image/bmp", Flags = Encoder | Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0x42, 0x4d } } },
            new CodecInfo { Clsid = Clsid(1), Format = ImageFormat.Jpeg.Guid, Name = "Built-in JPEG Codec", Description = "JPEG", Extension = "*.JPG;*.JPEG;*.JPE;*.JFIF", Mime = "image/jpeg", Flags = Encoder | Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0xff, 0xd8 } } },
            new CodecInfo { Clsid = Clsid(2), Format = ImageFormat.Gif.Guid, Name = "Built-in GIF Codec", Description = "GIF", Extension = "*.GIF", Mime = "image/gif", Flags = Encoder | Decoder | SupportBitmap | Builtin, Signatures = new[] { Encoding.ASCII.GetBytes("GIF89a"), Encoding.ASCII.GetBytes("GIF87a") } },
            new CodecInfo { Clsid = Clsid(3), Format = ImageFormat.Emf.Guid, Name = "Built-in EMF Codec", Description = "EMF", Extension = "*.EMF", Mime = "image/x-emf", Flags = Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x20, 0x45, 0x4d, 0x46 } },
                Masks = new[] { new byte[40].Concat(new byte[] { 0xff, 0xff, 0xff, 0xff }).ToArray() } }, // " EMF" at offset 40
            new CodecInfo { Clsid = Clsid(4), Format = ImageFormat.Wmf.Guid, Name = "Built-in WMF Codec", Description = "WMF", Extension = "*.WMF", Mime = "image/x-wmf", Flags = Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0xd7, 0xcd, 0xc6, 0x9a } } },
            new CodecInfo { Clsid = Clsid(5), Format = ImageFormat.Tiff.Guid, Name = "Built-in TIFF Codec", Description = "TIFF", Extension = "*.TIF;*.TIFF", Mime = "image/tiff", Flags = Encoder | Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0x49, 0x49 }, new byte[] { 0x4d, 0x4d } } },
            new CodecInfo { Clsid = Clsid(6), Format = ImageFormat.Png.Guid, Name = "Built-in PNG Codec", Description = "PNG", Extension = "*.PNG", Mime = "image/png", Flags = Encoder | Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a } } },
            new CodecInfo { Clsid = Clsid(7), Format = ImageFormat.Icon.Guid, Name = "Built-in ICO Codec", Description = "ICO", Extension = "*.ICO", Mime = "image/x-icon", Flags = Decoder | SupportBitmap | Builtin, Signatures = new[] { new byte[] { 0, 0, 1, 0 } } },
        };

        public static CodecInfo ByClsid(Guid clsid) => All.FirstOrDefault(c => c.Clsid == clsid);

        // Decoding

        public static int Decode(byte[] data, out GpImage image)
        {
            image = null;
            // GDI+ reports data it cannot decode as InvalidParameter from a stream (ArgumentException) and as OutOfMemory
            // from a file (GdipLoadImageFromFile).
            if (data.Length < 4) return SafeNativeMethods.SkiaGdiPlus.InvalidParameter;
            Guid raw;
            if (StartsWith(data, 0xff, 0xd8)) raw = ImageFormat.Jpeg.Guid;
            else if (StartsWith(data, 0x89, 0x50, 0x4e, 0x47)) raw = ImageFormat.Png.Guid;
            else if (StartsWith(data, (byte)'G', (byte)'I', (byte)'F')) raw = ImageFormat.Gif.Guid;
            else if (StartsWith(data, (byte)'B', (byte)'M')) raw = ImageFormat.Bmp.Guid;
            else if (StartsWith(data, 0, 0, 1, 0)) raw = ImageFormat.Icon.Guid;
            else if (StartsWith(data, 0x49, 0x49, 0x2a, 0) || StartsWith(data, 0x4d, 0x4d, 0, 0x2a)) return Tiff.Read(data, out image); // Skia has no TIFF decoder
            else return SafeNativeMethods.SkiaGdiPlus.InvalidParameter;

            // GDI+ decodes one image of an icon: the one nearest 16x16 (deepest colour first); Skia would take the largest.
            var skData = SKData.CreateCopy(raw == ImageFormat.Icon.Guid ? IconEntry(data) : data);
            var codec = SKCodec.Create(skData);
            if (codec == null) return SafeNativeMethods.SkiaGdiPlus.InvalidParameter;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var first = DecodeFrame(codec, info, 0);
            if (first == null)
            {
                codec.Dispose();
                return SafeNativeMethods.SkiaGdiPlus.InvalidParameter;
            }
            image = new GpImage(first, PixelFormat.Format32bppArgb, raw);
            // HasRealDpi is set by the readers below when the file states a resolution (GDI+).
            image.Flags |= (int)ImageFlags.ReadOnly | (int)ImageFlags.HasRealPixelSize | (int)ImageFlags.ColorSpaceRgb;
            try
            {
                if (raw == ImageFormat.Jpeg.Guid) ReadJpeg(data, codec, image);
                else if (raw == ImageFormat.Png.Guid) ReadPng(data, image);
                else if (raw == ImageFormat.Gif.Guid) ReadGif(data, codec, image, info);
                else if (raw == ImageFormat.Bmp.Guid) ReadBmp(data, image);
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException)
            {
                // malformed metadata: keep the pixels, as GDI+ does
            }
            image.Flags = (image.Flags & ~(int)ImageFlags.HasAlpha) | (GpImage.HasAlpha(image.Format) ? (int)ImageFlags.HasAlpha : 0);
            if (image.FrameDecoder == null) codec.Dispose();
            return SafeNativeMethods.SkiaGdiPlus.Ok;
        }

        // A one-image icon file holding the image GDI+ would pick from data's directory.
        static byte[] IconEntry(byte[] data)
        {
            int count = data.Length >= 6 ? BitConverter.ToUInt16(data, 4) : 0;
            if (count <= 1 || data.Length < 6 + 16 * count) return data;
            int best = 0, bestScore = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int w = data[e] == 0 ? 256 : data[e], h = data[e + 1] == 0 ? 256 : data[e + 1], bits = BitConverter.ToUInt16(data, e + 6);
                int score = (Math.Abs(w - 16) + Math.Abs(h - 16)) * 64 - bits;
                if (score < bestScore) { best = i; bestScore = score; }
            }
            int entry = 6 + 16 * best;
            int size = BitConverter.ToInt32(data, entry + 8), offset = BitConverter.ToInt32(data, entry + 12);
            if (offset < 0 || size <= 0 || offset + size > data.Length) return data;
            var result = new byte[6 + 16 + size];
            Array.Copy(data, 0, result, 0, 6);
            result[4] = 1; result[5] = 0;
            Array.Copy(data, entry, result, 6, 12);
            BitConverter.GetBytes(22).CopyTo(result, 18);
            Array.Copy(data, offset, result, 22, size);
            return result;
        }

        static SKBitmap DecodeFrame(SKCodec codec, SKImageInfo info, int frame)
        {
            var bitmap = new SKBitmap(info);
            var options = new SKCodecOptions(frame);
            var result = codec.GetPixels(info, bitmap.GetPixels(), options);
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                bitmap.Dispose();
                return null;
            }
            return bitmap;
        }

        static bool StartsWith(byte[] data, params byte[] prefix) => data.Length >= prefix.Length && prefix.Select((b, i) => data[i] == b).All(x => x);

        static void ReadJpeg(byte[] data, SKCodec codec, GpImage image)
        {
            image.Format = codec.Info.ColorType == SKColorType.Gray8 ? PixelFormat.Format8bppIndexed : PixelFormat.Format24bppRgb;
            if (image.Format == PixelFormat.Format8bppIndexed)
                image.Palette = Enumerable.Range(0, 256).Select(i => unchecked((int)0xff000000) | i << 16 | i << 8 | i).ToArray();
            // GDI+'s JPEG flags: the JPEG colour space (not RGB) and PartiallyScalable.
            image.Flags = (image.Flags & ~(int)ImageFlags.ColorSpaceRgb) | (int)ImageFlags.PartiallyScalable
                | (image.Format == PixelFormat.Format8bppIndexed ? (int)ImageFlags.ColorSpaceGray : (int)ImageFlags.ColorSpaceYcbcr);
            // Segments up to the image data: APP0 (JFIF density), APP1 (EXIF).
            int pos = 2;
            while (pos + 4 <= data.Length && data[pos] == 0xff)
            {
                byte marker = data[pos + 1];
                if (marker == 0xda || marker == 0xd9) break;
                int length = data[pos + 2] << 8 | data[pos + 3];
                int body = pos + 4;
                if (marker == 0xe0 && length >= 16 && Encoding.ASCII.GetString(data, body, 4) == "JFIF")
                {
                    int units = data[body + 7], x = data[body + 8] << 8 | data[body + 9], y = data[body + 10] << 8 | data[body + 11];
                    if (units == 1 && x > 0 && y > 0) { image.DpiX = x; image.DpiY = y;  image.Flags |= (int)ImageFlags.HasRealDpi; }
                    else if (units == 2 && x > 0 && y > 0) { image.DpiX = x * 2.54f; image.DpiY = y * 2.54f;  image.Flags |= (int)ImageFlags.HasRealDpi; }
                }
                else if (marker == 0xe1 && length > 8 && Encoding.ASCII.GetString(data, body, 4) == "Exif")
                {
                    Exif.Read(data, body + 6, length - 8, image.Properties);
                    var xres = image.Properties.FirstOrDefault(p => p.Id == 0x011a);
                    var yres = image.Properties.FirstOrDefault(p => p.Id == 0x011b);
                    var unit = image.Properties.FirstOrDefault(p => p.Id == 0x0128);
                    if (xres != null && yres != null && image.DpiX == 96)
                    {
                        float scale = unit != null && BitConverter.ToUInt16(unit.Value, 0) == 3 ? 2.54f : 1;
                        float rx = Rational(xres.Value), ry = Rational(yres.Value);
                        if (rx > 0 && ry > 0) { image.DpiX = rx * scale; image.DpiY = ry * scale; image.Flags |= (int)ImageFlags.HasRealDpi; }
                    }
                }
                pos += 2 + length;
            }
        }

        static float Rational(byte[] v) => BitConverter.ToUInt32(v, 4) == 0 ? 0 : (float)BitConverter.ToUInt32(v, 0) / BitConverter.ToUInt32(v, 4);

        static void ReadPng(byte[] data, GpImage image)
        {
            int colorType = data[25], bitDepth = data[24];
            int pos = 8;
            byte[] plte = null, trns = null;
            while (pos + 8 <= data.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                int body = pos + 8;
                if (type == "PLTE") plte = data.AsSpan(body, length).ToArray();
                else if (type == "tRNS") trns = data.AsSpan(body, length).ToArray();
                else if (type == "pHYs" && length >= 9 && data[body + 8] == 1)
                {
                    image.DpiX = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(body)) * 0.0254f;
                    image.DpiY = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(body + 4)) * 0.0254f;
                    image.Flags |= (int)ImageFlags.HasRealDpi;
                }
                else if (type == "gAMA" && length == 4)
                {
                    var value = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(value, 100000);
                    BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4), BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body)));
                    image.Properties.Add(new GpProperty { Id = 0x0301, Type = 5, Value = value });
                }
                else if (type == "IEND") break;
                pos = body + length + 4;
            }
            if (colorType == 3 && plte != null)
            {
                image.Format = bitDepth == 1 ? PixelFormat.Format1bppIndexed : bitDepth <= 4 ? PixelFormat.Format4bppIndexed : PixelFormat.Format8bppIndexed;
                int count = plte.Length / 3;
                image.Palette = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int alpha = trns != null && i < trns.Length ? trns[i] : 255;
                    image.Palette[i] = alpha << 24 | plte[i * 3] << 16 | plte[i * 3 + 1] << 8 | plte[i * 3 + 2];
                }
                if (trns != null) image.PaletteFlags = 1; // PaletteFlagsHasAlpha
            }
            else
            {
                bool alpha = colorType == 4 || colorType == 6 || trns != null;
                image.Format = bitDepth == 16
                    ? (alpha ? PixelFormat.Format64bppArgb : PixelFormat.Format48bppRgb)
                    : (alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
                if (colorType == 0 || colorType == 4) image.Flags |= (int)ImageFlags.ColorSpaceGray;
            }
        }

        static void ReadGif(byte[] data, SKCodec codec, GpImage image, SKImageInfo info)
        {
            image.Format = PixelFormat.Format8bppIndexed;
            int flags = data[10];
            var palette = new List<int>();
            if ((flags & 0x80) != 0)
            {
                int count = 2 << (flags & 7);
                for (int i = 0; i < count && 13 + i * 3 + 2 < data.Length; i++)
                    palette.Add(unchecked((int)0xff000000) | data[13 + i * 3] << 16 | data[14 + i * 3] << 8 | data[15 + i * 3]);
                image.Properties.Add(new GpProperty { Id = 0x5102, Type = 1, Value = data.AsSpan(13, count * 3).ToArray() });
                image.Properties.Add(new GpProperty { Id = 0x5103, Type = 1, Value = new[] { data[11] } });
            }
            image.Palette = palette.Count > 0 ? palette.ToArray() : GpImage.DefaultPalette(PixelFormat.Format8bppIndexed);
            int frames = codec.FrameCount;
            if (frames > 1)
            {
                image.FrameCount = frames;
                image.FrameDimension = System.Drawing.Imaging.FrameDimension.Time.Guid;
                image.FrameDecoder = index => DecodeFrame(codec, info, index);
                var delays = new byte[frames * 4];
                var frameInfo = codec.FrameInfo;
                for (int i = 0; i < frames; i++) BinaryPrimitives.WriteInt32LittleEndian(delays.AsSpan(i * 4), frameInfo[i].Duration / 10);
                image.Properties.Add(new GpProperty { Id = 0x5100, Type = 4, Value = delays });
                var loop = new byte[2];
                BinaryPrimitives.WriteInt16LittleEndian(loop, (short)Math.Max(0, codec.RepetitionCount));
                image.Properties.Add(new GpProperty { Id = 0x5101, Type = 3, Value = loop });
            }
            else
            {
                image.FrameDimension = System.Drawing.Imaging.FrameDimension.Page.Guid;
            }
        }

        static void ReadBmp(byte[] data, GpImage image)
        {
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(14));
            int bits = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(28));
            if (headerSize >= 40)
            {
                int ppmX = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(38)), ppmY = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(42));
                if (ppmX > 0 && ppmY > 0) { image.DpiX = (float)Math.Round(ppmX * 0.0254); image.DpiY = (float)Math.Round(ppmY * 0.0254);  image.Flags |= (int)ImageFlags.HasRealDpi; }
            }
            image.Format = bits switch
            {
                1 => PixelFormat.Format1bppIndexed,
                4 => PixelFormat.Format4bppIndexed,
                8 => PixelFormat.Format8bppIndexed,
                16 => PixelFormat.Format16bppRgb555,
                24 => PixelFormat.Format24bppRgb,
                _ => PixelFormat.Format32bppRgb,
            };
            if (bits <= 8)
            {
                int colors = headerSize >= 40 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(46)) : 0;
                if (colors == 0) colors = 1 << bits;
                int start = 14 + headerSize;
                image.Palette = new int[colors];
                for (int i = 0; i < colors; i++)
                    image.Palette[i] = unchecked((int)0xff000000) | data[start + i * 4 + 2] << 16 | data[start + i * 4 + 1] << 8 | data[start + i * 4];
            }
        }

        // Encoding

        public static int Encode(GpImage image, Guid clsid, IntPtr encoderParameters, Stream output)
        {
            var codec = ByClsid(clsid);
            if (codec == null || (codec.Flags & Encoder) == 0) return SafeNativeMethods.SkiaGdiPlus.FileNotFound; // GDI+: unknown encoder
            long quality = 75;
            if (encoderParameters != IntPtr.Zero)
            {
                var parameters = EncoderParameters.ConvertFromMemory(encoderParameters);
                foreach (var p in parameters.Param)
                {
                    if (p != null && p.Encoder.Guid == System.Drawing.Imaging.Encoder.Quality.Guid && p.NumberOfValues > 0)
                        quality = System.Runtime.InteropServices.Marshal.ReadInt32(GetValue(p));
                }
            }
            if (codec.Format == ImageFormat.Png.Guid) return EncodeSkia(image, SKEncodedImageFormat.Png, 100, output);
            if (codec.Format == ImageFormat.Jpeg.Guid) return EncodeJpeg(image, (int)Math.Clamp(quality, 0, 100), output);
            if (codec.Format == ImageFormat.Bmp.Guid) { Bmp.Write(image, output); return SafeNativeMethods.SkiaGdiPlus.Ok; }
            if (codec.Format == ImageFormat.Gif.Guid) { Gif.Write(image, output); return SafeNativeMethods.SkiaGdiPlus.Ok; }
            if (codec.Format == ImageFormat.Tiff.Guid) { Tiff.Write(image, output); return SafeNativeMethods.SkiaGdiPlus.Ok; }
            return SafeNativeMethods.SkiaGdiPlus.NotImplemented;
        }

        static IntPtr GetValue(EncoderParameter p) =>
            (IntPtr)typeof(EncoderParameter).GetField("_parameterValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(p);

        static int EncodeSkia(GpImage image, SKEncodedImageFormat format, int quality, Stream output)
        {
            // An image without alpha is written without it (a PNG of a 24bpp bitmap is RGB, and loads as 24bpp).
            using var pixmap = image.Pixels.PeekPixels();
            using var view = GpImage.EncodesAlpha(image.Format) ? null : new SKPixmap(pixmap.Info.WithAlphaType(SKAlphaType.Opaque), pixmap.GetPixels(), pixmap.RowBytes);
            using var data = (view ?? pixmap).Encode(format, quality);
            if (data == null) return SafeNativeMethods.SkiaGdiPlus.GenericError;
            data.SaveTo(output);
            return SafeNativeMethods.SkiaGdiPlus.Ok;
        }

        // JPEG through Skia, with the image's property items as an EXIF segment and its resolution in JFIF.
        static int EncodeJpeg(GpImage image, int quality, Stream output)
        {
            SKBitmap source = image.Pixels;
            SKBitmap opaque = null;
            if (source.AlphaType != SKAlphaType.Opaque)
            {
                // JPEG has no alpha: GDI+ composes on black.
                opaque = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(opaque);
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(source, 0, 0);
                source = opaque;
            }
            using (opaque)
            using (var data = source.Encode(SKEncodedImageFormat.Jpeg, quality))
            {
                if (data == null) return SafeNativeMethods.SkiaGdiPlus.GenericError;
                var bytes = data.ToArray();
                int pos = 2;
                // Skia writes SOI then (optionally) its own APP0; replace APP0's density and add EXIF after it.
                if (bytes.Length > 20 && bytes[2] == 0xff && bytes[3] == 0xe0)
                {
                    int length = bytes[4] << 8 | bytes[5];
                    bytes[13] = 1;
                    bytes[14] = (byte)((int)Math.Round(image.DpiX) >> 8); bytes[15] = (byte)Math.Round(image.DpiX);
                    bytes[16] = (byte)((int)Math.Round(image.DpiY) >> 8); bytes[17] = (byte)Math.Round(image.DpiY);
                    pos = 4 + length;
                }
                output.Write(bytes, 0, pos);
                var exif = Exif.Write(image.Properties);
                if (exif != null && exif.Length + 8 < 0xffff)
                {
                    int length = exif.Length + 8;
                    output.Write(new byte[] { 0xff, 0xe1, (byte)(length >> 8), (byte)length, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 }, 0, 10);
                    output.Write(exif, 0, exif.Length);
                }
                output.Write(bytes, pos, bytes.Length - pos);
            }
            return SafeNativeMethods.SkiaGdiPlus.Ok;
        }
    }

    // EXIF/TIFF tags <-> GDI+ property items (values in little-endian order, as GDI+ returns them).
    internal static class Exif
    {
        static readonly int[] TypeSize = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8 };

        public static void Read(byte[] data, int start, int length, List<GpProperty> properties)
        {
            bool big = data[start] == 'M';
            int U16(int o) => big ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start + o)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + o));
            int U32(int o) => big ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(start + o)) : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + o));
            var visited = new HashSet<int>();
            void ReadIfd(int offset, int idBase)
            {
                if (offset <= 0 || offset + 2 > length || !visited.Add(offset)) return;
                int count = U16(offset);
                for (int i = 0; i < count; i++)
                {
                    int e = offset + 2 + i * 12;
                    if (e + 12 > length) return;
                    int tag = U16(e), type = U16(e + 2), n = U32(e + 4);
                    if (type < 1 || type > 12) continue;
                    if (tag == 0x8769 || tag == 0x8825 || tag == 0xa005) { ReadIfd(U32(e + 8), 0); }
                    int size = TypeSize[type] * n;
                    if (size < 0 || size > length) continue;
                    int valueOffset = size <= 4 ? e + 8 : U32(e + 8);
                    if (valueOffset + size > length) continue;
                    var value = data.AsSpan(start + valueOffset, size).ToArray();
                    if (big) Swap(value, type);
                    int id = tag + idBase;
                    if (tag == 0x8769 || tag == 0x8825 || tag == 0xa005) continue; // offsets are not exposed
                    properties.RemoveAll(p => p.Id == id);
                    properties.Add(new GpProperty { Id = id, Type = (short)(type == 9 ? 4 : type == 10 ? 5 : type), Value = value });
                }
                int next = U32(offset + 2 + count * 12);
                if (idBase == 0 && next > 0) ReadThumbnailIfd(next);
            }
            void ReadThumbnailIfd(int offset)
            {
                // IFD1: GDI+ exposes the thumbnail tags as PropertyTagThumbnail* (0x50xx) and its data as 0x501b.
                if (offset + 2 > length || !visited.Add(offset)) return;
                int count = U16(offset), jpegOffset = -1, jpegLength = 0;
                for (int i = 0; i < count; i++)
                {
                    int e = offset + 2 + i * 12;
                    if (e + 12 > length) return;
                    int tag = U16(e);
                    if (tag == 0x0201) jpegOffset = U32(e + 8);
                    else if (tag == 0x0202) jpegLength = U32(e + 8);
                }
                if (jpegOffset > 0 && jpegLength > 0 && jpegOffset + jpegLength <= length)
                    properties.Add(new GpProperty { Id = 0x501b, Type = 1, Value = data.AsSpan(start + jpegOffset, jpegLength).ToArray() });
            }
            if (U16(2) != 42) return;
            ReadIfd(U32(4), 0);
        }

        static void Swap(byte[] value, int type)
        {
            int unit = type == 5 || type == 10 ? 4 : TypeSize[type];
            if (unit < 2) return;
            for (int i = 0; i + unit <= value.Length; i += unit) Array.Reverse(value, i, unit);
        }

        // EXIF tags (exposure, ExifVersion, dates, ...) go in the EXIF IFD; the rest, Windows' XP tags (0x9c9b-0x9c9f)
        // included, in IFD0.
        static bool IsExifTag(int id) => (id >= 0x829a && id <= 0x8830 && id != 0x8298 && id != 0x8769 && id != 0x8825)
            || (id >= 0x9000 && id <= 0xa4ff && !(id >= 0x9c9b && id <= 0x9c9f));
        static bool IsGpsTag(int id) => id >= 0 && id <= 0x1f;

        public static byte[] Write(List<GpProperty> properties)
        {
            // 0x5000-0x5fff are GDI+'s own properties (thumbnail, GIF frames, JPEG tables), not TIFF tags.
            var main = properties.Where(p => p.Id >= 0x100 && (p.Id < 0x5000 || p.Id > 0x5fff) && !IsExifTag(p.Id) && p.Value != null).OrderBy(p => p.Id).ToList();
            var exif = properties.Where(p => IsExifTag(p.Id) && p.Value != null).OrderBy(p => p.Id).ToList();
            if (main.Count == 0 && exif.Count == 0) return null;
            using var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write(8);
            int ifd0Entries = main.Count + (exif.Count > 0 ? 1 : 0);
            int ifd0Size = 2 + ifd0Entries * 12 + 4;
            int dataStart = 8 + ifd0Size;
            int ifd0DataSize = main.Sum(p => p.Value.Length > 4 ? (p.Value.Length + 1) & ~1 : 0);
            int exifIfdOffset = dataStart + ifd0DataSize;
            WriteIfd(w, main, exif.Count > 0 ? exifIfdOffset : -1, dataStart);
            if (exif.Count > 0)
            {
                ms.Position = exifIfdOffset;
                WriteIfd(w, exif, -1, exifIfdOffset + 2 + exif.Count * 12 + 4);
            }
            return ms.ToArray();
        }

        static void WriteIfd(BinaryWriter w, List<GpProperty> tags, int exifOffset, int dataOffset)
        {
            var entries = tags.Select(t => (Id: t.Id, Type: (int)t.Type, Value: t.Value)).ToList();
            if (exifOffset >= 0) entries.Add((0x8769, 4, BitConverter.GetBytes(exifOffset)));
            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            var start = w.BaseStream.Position;
            w.Write((ushort)entries.Count);
            var data = new MemoryStream();
            foreach (var (id, type, value) in entries)
            {
                int unit = type >= 1 && type <= 12 ? TypeSize[type] : 1;
                w.Write((ushort)id); w.Write((ushort)type); w.Write(unit == 0 ? value.Length : value.Length / Math.Max(unit, 1));
                if (value.Length <= 4)
                {
                    var padded = new byte[4];
                    value.CopyTo(padded, 0);
                    w.Write(padded);
                }
                else
                {
                    w.Write((int)(dataOffset + data.Length));
                    data.Write(value, 0, value.Length);
                    if (data.Length % 2 == 1) data.WriteByte(0);
                }
            }
            w.Write(0);
            w.Write(data.ToArray());
        }
    }

    // BMP encoder: GDI+ writes the image's own depth (24bpp, 32bpp, indexed with its palette).
    internal static class Bmp
    {
        public static unsafe void Write(GpImage image, Stream output)
        {
            var format = image.Format;
            if (!GpImage.IsIndexed(format) && format != PixelFormat.Format24bppRgb) format = GpImage.EncodesAlpha(format) ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            int bpp = GpImage.BitsPerPixel(format);
            int stride = GpImage.Stride(image.Width, format);
            int paletteSize = GpImage.IsIndexed(format) ? (image.Palette?.Length ?? 0) * 4 : 0;
            int offset = 14 + 40 + paletteSize;
            var pixels = new byte[stride * image.Height];
            fixed (byte* p = pixels)
                image.ReadPixels(new Rectangle(0, 0, image.Width, image.Height), format, p + (long)stride * (image.Height - 1), -stride);
            var w = new BinaryWriter(output);
            w.Write((byte)'B'); w.Write((byte)'M'); w.Write(offset + pixels.Length); w.Write(0); w.Write(offset);
            w.Write(40); w.Write(image.Width); w.Write(image.Height); w.Write((short)1); w.Write((short)bpp); w.Write(0); w.Write(pixels.Length);
            w.Write((int)Math.Round(image.DpiX / 0.0254)); w.Write((int)Math.Round(image.DpiY / 0.0254));
            w.Write(paletteSize / 4); w.Write(0);
            if (paletteSize > 0) foreach (int c in image.Palette) w.Write(c & 0xffffff);
            w.Write(pixels);
            w.Flush();
        }
    }

    // GIF encoder: an indexed image keeps its palette; others are mapped to GDI+'s halftone palette, fully transparent
    // pixels to a transparent entry.
    internal static class Gif
    {
        public static unsafe void Write(GpImage image, Stream output)
        {
            int width = image.Width, height = image.Height;
            int[] palette;
            var indices = new byte[width * height];
            int transparent = -1;
            if (image.Format == PixelFormat.Format8bppIndexed && image.Palette != null && image.Palette.Length <= 256)
            {
                palette = image.Palette;
                fixed (byte* p = indices) image.ReadPixels(new Rectangle(0, 0, width, height), PixelFormat.Format8bppIndexed, p, width);
            }
            else
            {
                palette = GpImage.DefaultPalette(PixelFormat.Format8bppIndexed);
                var lookup = new Dictionary<int, byte>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int argb = image.GetArgb(x, y);
                        if (((argb >> 24) & 255) < 128) { transparent = 255; indices[y * width + x] = 255; continue; }
                        // halftone: the nearest of the 6x6x6 cube
                        int r = (argb >> 16 & 255), g = (argb >> 8 & 255), b = argb & 255;
                        indices[y * width + x] = (byte)(40 + (r + 25) / 51 * 36 + (g + 25) / 51 * 6 + (b + 25) / 51);
                    }
                if (transparent >= 0) palette[255] = 0;
            }
            var w = new BinaryWriter(output);
            w.Write(Encoding.ASCII.GetBytes("GIF89a"));
            w.Write((ushort)width); w.Write((ushort)height);
            w.Write((byte)0xf7); w.Write((byte)0); w.Write((byte)0);
            for (int i = 0; i < 256; i++)
            {
                int c = i < palette.Length ? palette[i] : 0;
                w.Write((byte)(c >> 16)); w.Write((byte)(c >> 8)); w.Write((byte)c);
            }
            if (transparent < 0 && image.Palette != null && image.Format == PixelFormat.Format8bppIndexed)
                for (int i = 0; i < palette.Length; i++) if (((palette[i] >> 24) & 255) == 0) { transparent = i; break; }
            if (transparent >= 0)
            {
                w.Write(new byte[] { 0x21, 0xf9, 4, 1, 0, 0, (byte)transparent, 0 });
            }
            w.Write((byte)0x2c); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)height); w.Write((byte)0);
            Lzw(w, indices, 8);
            w.Write((byte)0x3b);
            w.Flush();
        }

        static void Lzw(BinaryWriter w, byte[] data, int minCodeSize)
        {
            w.Write((byte)minCodeSize);
            int clear = 1 << minCodeSize, end = clear + 1, next = end + 1, codeSize = minCodeSize + 1;
            var table = new Dictionary<int, int>();
            var block = new List<byte>();
            int bitBuffer = 0, bitCount = 0;
            void Emit(int code)
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    block.Add((byte)bitBuffer);
                    bitBuffer >>= 8;
                    bitCount -= 8;
                    if (block.Count == 255) { w.Write((byte)255); w.Write(block.ToArray()); block.Clear(); }
                }
            }
            Emit(clear);
            int prefix = -1;
            foreach (byte b in data)
            {
                if (prefix < 0) { prefix = b; continue; }
                int key = prefix << 8 | b;
                if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix);
                if (next < 4096)
                {
                    table[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    Emit(clear);
                    table.Clear();
                    next = end + 1;
                    codeSize = minCodeSize + 1;
                }
                prefix = b;
            }
            if (prefix >= 0) Emit(prefix);
            Emit(end);
            if (bitCount > 0) block.Add((byte)bitBuffer);
            if (block.Count > 0) { w.Write((byte)block.Count); w.Write(block.ToArray()); }
            w.Write((byte)0);
        }
    }

    // TIFF: a baseline reader (strips; no compression, LZW with or without the horizontal predictor, PackBits; grey,
    // palette, RGB and RGBA; every page a frame) and an encoder (uncompressed 24bpp RGB or 32bpp RGBA, one strip).
    internal static class Tiff
    {
        public static int Read(byte[] data, out GpImage image)
        {
            image = null;
            bool big = data[0] == 'M';
            int U16(int o) => big ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o));
            int U32(int o) => big ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(o)) : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o));
            var pages = new List<Dictionary<int, int[]>>();
            int offset = U32(4);
            var seen = new HashSet<int>();
            while (offset > 0 && offset + 2 <= data.Length && seen.Add(offset))
            {
                int count = U16(offset);
                var tags = new Dictionary<int, int[]>();
                for (int i = 0; i < count; i++)
                {
                    int e = offset + 2 + i * 12;
                    if (e + 12 > data.Length) break;
                    int tag = U16(e), type = U16(e + 2), n = U32(e + 4);
                    int size = type == 3 ? 2 : type == 4 ? 4 : type == 1 ? 1 : 0;
                    if (size == 0 || n <= 0 || n > 1 << 24) continue;
                    int at = size * n <= 4 ? e + 8 : U32(e + 8);
                    var values = new int[n];
                    for (int k = 0; k < n && at + (k + 1) * size <= data.Length; k++)
                        values[k] = size == 2 ? U16(at + k * 2) : size == 4 ? U32(at + k * 4) : data[at + k];
                    tags[tag] = values;
                }
                pages.Add(tags);
                offset = offset + 2 + count * 12 + 4 <= data.Length ? U32(offset + 2 + count * 12) : 0;
            }
            if (pages.Count == 0) return SafeNativeMethods.SkiaGdiPlus.InvalidParameter;
            var first = DecodePage(data, pages[0], big, out PixelFormat format, out int[] palette);
            if (first == null) return SafeNativeMethods.SkiaGdiPlus.NotImplemented;
            image = new GpImage(first, format, ImageFormat.Tiff.Guid) { Palette = palette, PaletteFlags = 0 };
            image.Flags |= (int)ImageFlags.ReadOnly | (int)ImageFlags.HasRealPixelSize | (int)ImageFlags.ColorSpaceRgb;
            var tags0 = pages[0];
            Exif.Read(data, 0, data.Length, image.Properties);
            var xres = image.Properties.FirstOrDefault(p => p.Id == 0x011a);
            var yres = image.Properties.FirstOrDefault(p => p.Id == 0x011b);
            if (xres != null && yres != null)
            {
                float scale = tags0.TryGetValue(0x128, out var unit) && unit[0] == 3 ? 2.54f : 1;
                float rx = Rational(xres.Value), ry = Rational(yres.Value);
                if (rx > 0 && ry > 0) { image.DpiX = rx * scale; image.DpiY = ry * scale; image.Flags |= (int)ImageFlags.HasRealDpi; }
            }
            image.FrameDimension = FrameDimension.Page.Guid;
            if (pages.Count > 1)
            {
                image.FrameCount = pages.Count;
                image.FrameDecoder = index => DecodePage(data, pages[index], big, out _, out _) ?? new SKBitmap(1, 1);
            }
            return SafeNativeMethods.SkiaGdiPlus.Ok;
        }

        static float Rational(byte[] v) => BitConverter.ToUInt32(v, 4) == 0 ? 0 : (float)BitConverter.ToUInt32(v, 0) / BitConverter.ToUInt32(v, 4);

        static SKBitmap DecodePage(byte[] data, Dictionary<int, int[]> tags, bool big, out PixelFormat format, out int[] palette)
        {
            format = PixelFormat.Format24bppRgb;
            palette = null;
            int T(int tag, int fallback) => tags.TryGetValue(tag, out var v) && v.Length > 0 ? v[0] : fallback;
            int width = T(0x100, 0), height = T(0x101, 0);
            int compression = T(0x103, 1), photometric = T(0x106, 2), samples = T(0x115, 1), predictor = T(0x13d, 1), planar = T(0x11c, 1);
            int bits = T(0x102, 1);
            if (width <= 0 || height <= 0 || planar != 1 || tags.ContainsKey(0x142)) return null; // tiles, planar: not supported
            if (compression != 1 && compression != 5 && compression != 32773) return null;
            if (!tags.TryGetValue(0x111, out var offsets) || !tags.TryGetValue(0x117, out var counts)) return null;
            int rowsPerStrip = T(0x116, height);
            int bytesPerRow = (width * samples * bits + 7) / 8;
            var raw = new byte[bytesPerRow * height];
            int written = 0;
            for (int s = 0; s < offsets.Length && written < raw.Length; s++)
            {
                if (offsets[s] < 0 || offsets[s] + counts[s] > data.Length) return null;
                var strip = data.AsSpan(offsets[s], counts[s]);
                byte[] decoded = compression == 5 ? Lzw(strip) : compression == 32773 ? PackBits(strip) : strip.ToArray();
                int expected = Math.Min(bytesPerRow * Math.Min(rowsPerStrip, height - s * rowsPerStrip), raw.Length - written);
                Array.Copy(decoded, 0, raw, written, Math.Min(expected, decoded.Length));
                if (predictor == 2 && bits == 8)
                    for (int r = 0; r < expected / bytesPerRow; r++)
                        for (int i = samples; i < bytesPerRow; i++) raw[written + r * bytesPerRow + i] += raw[written + r * bytesPerRow + i - samples];
                written += expected;
            }
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            int extra = tags.TryGetValue(0x152, out var es) && es.Length > 0 ? es[0] : 0;
            if (photometric == 3 && tags.TryGetValue(0x140, out var map))
            {
                int n = 1 << bits;
                palette = new int[n];
                for (int i = 0; i < n && i + 2 * n < map.Length; i++)
                    palette[i] = unchecked((int)0xff000000) | (map[i] >> 8) << 16 | (map[i + n] >> 8) << 8 | (map[i + 2 * n] >> 8);
                format = bits == 1 ? PixelFormat.Format1bppIndexed : bits == 4 ? PixelFormat.Format4bppIndexed : PixelFormat.Format8bppIndexed;
            }
            else if (photometric <= 1 && samples == 2 && bits == 8)
            {
                format = PixelFormat.Format32bppArgb; // grey and alpha
            }
            else if (photometric <= 1)
            {
                int n = 1 << Math.Min(bits, 8);
                palette = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int v = n == 1 ? 0 : i * 255 / (n - 1);
                    if (photometric == 0) v = 255 - v;
                    palette[i] = unchecked((int)0xff000000) | v << 16 | v << 8 | v;
                }
                format = bits == 1 ? PixelFormat.Format1bppIndexed : bits == 4 ? PixelFormat.Format4bppIndexed : PixelFormat.Format8bppIndexed;
                if (samples > 1) return null;
            }
            else if (samples >= 4 || extra != 0)
            {
                format = PixelFormat.Format32bppArgb;
            }
            if (bits != 8 && photometric == 2) return null;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int o = y * bytesPerRow;
                    SKColor c;
                    if (palette != null)
                    {
                        int index = bits == 8 ? raw[o + x] : bits == 4 ? (x % 2 == 0 ? raw[o + x / 2] >> 4 : raw[o + x / 2] & 15) : (raw[o + x / 8] >> (7 - x % 8)) & 1;
                        c = new SKColor((uint)palette[Math.Min(index, palette.Length - 1)]);
                    }
                    else if (photometric <= 1)
                    {
                        int p = o + x * 2;
                        byte v = photometric == 0 ? (byte)(255 - raw[p]) : raw[p];
                        c = new SKColor(v, v, v, raw[p + 1]);
                    }
                    else
                    {
                        int p = o + x * samples;
                        c = new SKColor(raw[p], raw[p + 1], raw[p + 2], samples >= 4 ? raw[p + 3] : (byte)255);
                    }
                    bitmap.SetPixel(x, y, c);
                }
            return bitmap;
        }

        static byte[] PackBits(ReadOnlySpan<byte> src)
        {
            var output = new List<byte>(src.Length * 2);
            int i = 0;
            while (i < src.Length)
            {
                sbyte n = (sbyte)src[i++];
                if (n >= 0) { for (int k = 0; k <= n && i < src.Length; k++) output.Add(src[i++]); }
                else if (n != -128 && i < src.Length) { byte b = src[i++]; for (int k = 0; k <= -n; k++) output.Add(b); }
            }
            return output.ToArray();
        }

        // TIFF LZW (MSB-first codes, early change).
        static byte[] Lzw(ReadOnlySpan<byte> src)
        {
            var output = new List<byte>(src.Length * 3);
            var table = new List<byte[]>(4096);
            void Reset() { table.Clear(); for (int i = 0; i < 256; i++) table.Add(new[] { (byte)i }); table.Add(null); table.Add(null); }
            Reset();
            int bitPos = 0, codeSize = 9;
            byte[] previous = null;
            while (true)
            {
                if ((bitPos + codeSize + 7) / 8 > src.Length) break;
                int code = 0;
                for (int k = 0; k < codeSize; k++, bitPos++) code = code << 1 | (src[bitPos >> 3] >> (7 - (bitPos & 7)) & 1);
                if (code == 257) break;
                if (code == 256) { Reset(); codeSize = 9; previous = null; continue; }
                byte[] entry;
                if (code < table.Count && table[code] != null) entry = table[code];
                else if (previous != null) { entry = new byte[previous.Length + 1]; previous.CopyTo(entry, 0); entry[^1] = previous[0]; }
                else break;
                output.AddRange(entry);
                if (previous != null)
                {
                    var added = new byte[previous.Length + 1];
                    previous.CopyTo(added, 0);
                    added[^1] = entry[0];
                    table.Add(added);
                }
                previous = entry;
                if (table.Count + 1 >= 1 << codeSize && codeSize < 12) codeSize++;
            }
            return output.ToArray();
        }

        public static unsafe void Write(GpImage image, Stream output)
        {
            bool alpha = GpImage.EncodesAlpha(image.Format);
            int channels = alpha ? 4 : 3, width = image.Width, height = image.Height;
            var pixels = new byte[width * height * channels];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int argb = image.GetArgb(x, y), o = (y * width + x) * channels;
                    pixels[o] = (byte)(argb >> 16); pixels[o + 1] = (byte)(argb >> 8); pixels[o + 2] = (byte)argb;
                    if (alpha) pixels[o + 3] = (byte)(argb >> 24);
                }
            var w = new BinaryWriter(output);
            int entries = alpha ? 12 : 11;
            int ifd = 8, extra = ifd + 2 + entries * 12 + 4;
            int bitsOffset = extra, resOffset = bitsOffset + channels * 2, dataOffset = resOffset + 16;
            w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write(ifd);
            w.Write((ushort)entries);
            void Entry(int tag, int type, int count, int value) { w.Write((ushort)tag); w.Write((ushort)type); w.Write(count); w.Write(value); }
            Entry(0x100, 4, 1, width);
            Entry(0x101, 4, 1, height);
            Entry(0x102, 3, channels, bitsOffset);
            Entry(0x103, 3, 1, 1);
            Entry(0x106, 3, 1, 2);
            Entry(0x111, 4, 1, dataOffset);
            Entry(0x115, 3, 1, channels);
            Entry(0x116, 4, 1, height);
            Entry(0x117, 4, 1, pixels.Length);
            Entry(0x11a, 5, 1, resOffset);
            Entry(0x11b, 5, 1, resOffset + 8);
            if (alpha) Entry(0x152, 3, 1, 2);
            w.Write(0);
            for (int i = 0; i < channels; i++) w.Write((ushort)8);
            w.Write((int)Math.Round(image.DpiX * 100)); w.Write(100); w.Write((int)Math.Round(image.DpiY * 100)); w.Write(100);
            w.Write(pixels);
            w.Flush();
        }
    }
}
