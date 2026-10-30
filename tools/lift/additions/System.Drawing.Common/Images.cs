// webForCore: the GDI+ flat API for images and bitmaps, on SkiaSharp (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace System.Drawing
{
    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        static GpImage Image(HandleRef image) => Get<GpImage>(image);
        static GpImage Image(IntPtr image) => Get<GpImage>(image);

        // Loading

        static int Load(byte[] data, out IntPtr image)
        {
            image = IntPtr.Zero;
            int status = Codecs.Decode(data, out GpImage decoded);
            if (status == Ok) image = NewHandle(decoded);
            return status;
        }

        internal static int GdipLoadImageFromDelegate_linux(StreamGetHeaderDelegate getHeader, StreamGetBytesDelegate getBytes, StreamPutBytesDelegate putBytes,
            StreamSeekDelegate doSeek, StreamCloseDelegate close, StreamSizeDelegate size, out IntPtr image)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            fixed (byte* p = buffer)
            {
                int read;
                while ((read = getBytes(p, buffer.Length, false)) > 0) ms.Write(buffer, 0, read);
            }
            return Load(ms.ToArray(), out image);
        }

        internal static int GdipLoadImageFromFile(string filename, out IntPtr image)
        {
            image = IntPtr.Zero;
            if (filename == null) return InvalidParameter;
            if (!File.Exists(filename)) return InvalidParameter; // GDI+: a missing file is an invalid parameter (ArgumentException)
            int status = Load(File.ReadAllBytes(filename), out image);
            return status == InvalidParameter ? OutOfMemory : status; // GDI+: undecodable files are "Out of memory"
        }

        // HICON and HBITMAP are image handles, as with libgdiplus (System.Drawing's Unix Icon uses its bitmap's handle as
        // its HICON). Each holds its own copy, so it outlives the bitmap it came from. A bitmap made from one is a memory
        // bitmap with the flags GDI+ gives it (0x52010).
        internal static int GdipCreateHICONFromBitmap(HandleRef nativeBitmap, out IntPtr hicon)
        {
            hicon = IntPtr.Zero;
            var img = Image(nativeBitmap);
            if (img == null) return InvalidParameter;
            hicon = NewHandle(HandleCopy(img, PixelFormat.Format32bppArgb, 0));
            return Ok;
        }

        internal static int GdipCreateHBITMAPFromBitmap(HandleRef nativeBitmap, out IntPtr hbitmap, int argbBackground)
        {
            hbitmap = IntPtr.Zero;
            var img = Image(nativeBitmap);
            if (img == null) return InvalidParameter;
            // An HBITMAP has no alpha: the image is composed on the background colour (a COLORREF, 0x00BBGGRR).
            var copy = HandleCopy(img, PixelFormat.Format32bppRgb, 0);
            using (var canvas = new SKCanvas(copy.Pixels))
            {
                canvas.Clear(new SKColor((byte)argbBackground, (byte)(argbBackground >> 8), (byte)(argbBackground >> 16)));
                canvas.DrawBitmap(img.Pixels, 0, 0);
            }
            hbitmap = NewHandle(copy);
            return Ok;
        }

        internal static int GdipCreateBitmapFromHICON(IntPtr hicon, out IntPtr bitmap)
        {
            bitmap = IntPtr.Zero;
            var img = hicon == IntPtr.Zero ? null : Get<GpImage>(new HandleRef(null, hicon));
            if (img == null) return InvalidParameter;
            bitmap = NewHandle(HandleCopy(img, PixelFormat.Format32bppArgb, 0x52010));
            return Ok;
        }

        internal static int GdipCreateBitmapFromHBITMAP(IntPtr hbitmap, IntPtr hpalette, out IntPtr bitmap)
        {
            bitmap = IntPtr.Zero;
            var img = hbitmap == IntPtr.Zero ? null : Get<GpImage>(new HandleRef(null, hbitmap));
            if (img == null) return InvalidParameter;
            bitmap = NewHandle(HandleCopy(img, PixelFormat.Format32bppRgb, 0x52010));
            return Ok;
        }

        static GpImage HandleCopy(GpImage img, PixelFormat format, int flags)
        {
            var frame = img.Pixels.Copy();
            if (frame == null) throw new OutOfMemoryException();
            return new GpImage(frame, format, ImageFormat.MemoryBmp.Guid) { Flags = flags, DpiX = img.DpiX, DpiY = img.DpiY };
        }

        internal static int GdipLoadImageFromFileICM(string filename, out IntPtr image) => GdipLoadImageFromFile(filename, out image);
        internal static int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap) => GdipLoadImageFromFile(filename, out bitmap);
        internal static int GdipCreateBitmapFromFileICM(string filename, out IntPtr bitmap) => GdipLoadImageFromFile(filename, out bitmap);

        // Saving

        internal static int GdipSaveImageToDelegate_linux(IntPtr image, StreamGetBytesDelegate getBytes, StreamPutBytesDelegate putBytes,
            StreamSeekDelegate doSeek, StreamCloseDelegate close, StreamSizeDelegate size, ref Guid encoderClsID, IntPtr encoderParameters)
        {
            var img = Image(image);
            if (img == null) return InvalidParameter;
            using var ms = new MemoryStream();
            int status = Codecs.Encode(img, encoderClsID, encoderParameters, ms);
            if (status != Ok) return status;
            var bytes = ms.GetBuffer();
            fixed (byte* p = bytes)
            {
                int written = 0, length = (int)ms.Length;
                while (written < length)
                {
                    int n = putBytes(p + written, length - written);
                    if (n <= 0) return GenericError;
                    written += n;
                }
            }
            return Ok;
        }

        internal static int GdipSaveImageToFile(IntPtr image, string filename, ref Guid encoderClsID, IntPtr encoderParameters)
        {
            var img = Image(image);
            if (img == null || filename == null) return InvalidParameter;
            using var ms = new MemoryStream();
            int status = Codecs.Encode(img, encoderClsID, encoderParameters, ms);
            if (status != Ok) return status;
            try
            {
                File.WriteAllBytes(filename, ms.ToArray());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Win32Error;
            }
            return Ok;
        }

        // Codecs

        internal static int GdipGetImageDecodersSize(out int numDecoders, out int size) => CodecListSize(c => (c.Flags & 2) != 0, out numDecoders, out size);
        internal static int GdipGetImageDecoders(int numDecoders, int size, IntPtr decoders) => CodecList(c => (c.Flags & 2) != 0, numDecoders, size, decoders);
        internal static int GdipGetImageEncodersSize(out int numEncoders, out int size) => CodecListSize(c => (c.Flags & 1) != 0, out numEncoders, out size);
        internal static int GdipGetImageEncoders(int numEncoders, int size, IntPtr encoders) => CodecList(c => (c.Flags & 1) != 0, numEncoders, size, encoders);

        static int StringBytes(string s) => s == null ? 0 : (s.Length + 1) * 2;

        static int CodecListSize(Func<Codecs.CodecInfo, bool> filter, out int count, out int size)
        {
            var codecs = Codecs.All.Where(filter).ToArray();
            count = codecs.Length;
            size = codecs.Sum(c => Marshal.SizeOf<ImageCodecInfoPrivate>() + StringBytes(c.Name) + StringBytes(c.Description) + StringBytes(c.Extension) + StringBytes(c.Mime)
                + c.Signatures.Sum(s => s.Length * 2));
            return Ok;
        }

        // The ImageCodecInfo structures, followed by their strings and signatures, in the caller's buffer.
        static int CodecList(Func<Codecs.CodecInfo, bool> filter, int count, int size, IntPtr buffer)
        {
            var codecs = Codecs.All.Where(filter).ToArray();
            CodecListSize(filter, out int expectedCount, out int expectedSize);
            if (count != expectedCount || size < expectedSize || buffer == IntPtr.Zero) return InvalidParameter;
            int structSize = Marshal.SizeOf<ImageCodecInfoPrivate>();
            byte* extra = (byte*)buffer + structSize * count;
            IntPtr Put(string s)
            {
                if (s == null) return IntPtr.Zero;
                var at = (IntPtr)extra;
                var bytes = Encoding.Unicode.GetBytes(s + "\0");
                Marshal.Copy(bytes, 0, at, bytes.Length);
                extra += bytes.Length;
                return at;
            }
            IntPtr PutBytes(byte[] b)
            {
                var at = (IntPtr)extra;
                Marshal.Copy(b, 0, at, b.Length);
                extra += b.Length;
                return at;
            }
            for (int i = 0; i < codecs.Length; i++)
            {
                var c = codecs[i];
                int sigSize = c.Signatures.Max(s => s.Length);
                var info = new ImageCodecInfoPrivate
                {
                    Clsid = c.Clsid,
                    FormatID = c.Format,
                    CodecName = Put(c.Name),
                    DllName = IntPtr.Zero,
                    FormatDescription = Put(c.Description),
                    FilenameExtension = Put(c.Extension),
                    MimeType = Put(c.Mime),
                    Flags = c.Flags,
                    Version = 1,
                    SigCount = c.Signatures.Length,
                    SigSize = sigSize,
                };
                info.SigPattern = PutBytes(c.Signatures.SelectMany(s => s.Concat(new byte[sigSize - s.Length])).ToArray());
                info.SigMask = PutBytes(c.Signatures.SelectMany((s, i) => (c.Masks?[i] ?? Enumerable.Repeat((byte)0xff, s.Length).ToArray()).Concat(new byte[sigSize - s.Length])).ToArray());
                Marshal.StructureToPtr(info, (IntPtr)((byte*)buffer + i * structSize), false);
            }
            return Ok;
        }

        internal static int GdipGetEncoderParameterListSize(HandleRef image, ref Guid encoder, out int size)
        {
            size = 0;
            return Image(image) == null ? InvalidParameter : NotImplemented;
        }

        // Image information

        internal static int GdipDisposeImage(HandleRef image) => Free(image);
        internal static int GdipDisposeImage(IntPtr image) => Free(image);

        internal static int GdipCloneImage(IntPtr image, out IntPtr imageclone)
        {
            imageclone = IntPtr.Zero;
            var img = Image(image);
            if (img == null) return InvalidParameter;
            imageclone = NewHandle(img.Clone());
            return Ok;
        }

        internal static int GdipGetImageType(HandleRef image, out int type) => GdipGetImageType(image.Handle, out type);
        internal static int GdipGetImageType(IntPtr image, out int type)
        {
            type = 1; // ImageTypeBitmap
            return Image(image) == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageWidth(HandleRef image, out int width)
        {
            var img = Image(image);
            width = img?.Width ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageHeight(HandleRef image, out int height)
        {
            var img = Image(image);
            height = img?.Height ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageDimension(HandleRef image, out float width, out float height)
        {
            var img = Image(image);
            width = img?.Width ?? 0;
            height = img?.Height ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageBounds(IntPtr image, out RectangleF source, ref GraphicsUnit unit)
        {
            var img = Image(image);
            source = img == null ? default : new RectangleF(0, 0, img.Width, img.Height);
            unit = GraphicsUnit.Pixel;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageHorizontalResolution(HandleRef image, out float horzRes)
        {
            var img = Image(image);
            horzRes = img?.DpiX ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageVerticalResolution(HandleRef image, out float vertRes)
        {
            var img = Image(image);
            vertRes = img?.DpiY ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageFlags(HandleRef image, out int flags)
        {
            var img = Image(image);
            flags = img?.Flags ?? 0;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImageRawFormat(HandleRef image, ref Guid format)
        {
            var img = Image(image);
            if (img == null) return InvalidParameter;
            format = img.RawFormat;
            return Ok;
        }

        internal static int GdipGetImagePixelFormat(HandleRef image, out PixelFormat format)
        {
            var img = Image(image);
            format = img?.Format ?? PixelFormat.Undefined;
            return img == null ? InvalidParameter : Ok;
        }

        // Palettes: ColorPalette's native form is { int Flags; int Count; int Entries[Count]; }.

        internal static int GdipGetImagePaletteSize(IntPtr image, out int size)
        {
            var img = Image(image);
            size = img == null ? 0 : 8 + 4 * (img.Palette?.Length ?? 0);
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetImagePalette(IntPtr image, IntPtr palette, int size)
        {
            var img = Image(image);
            if (img == null || palette == IntPtr.Zero) return InvalidParameter;
            int count = img.Palette?.Length ?? 0;
            if (size < 8 + 4 * count) return InvalidParameter;
            Marshal.WriteInt32(palette, img.PaletteFlags);
            Marshal.WriteInt32(palette, 4, count);
            if (count > 0) Marshal.Copy(img.Palette, 0, palette + 8, count);
            return Ok;
        }

        internal static int GdipSetImagePalette(IntPtr image, IntPtr palette)
        {
            var img = Image(image);
            if (img == null || palette == IntPtr.Zero) return InvalidParameter;
            int count = Marshal.ReadInt32(palette, 4);
            if (count < 0 || count > 256) return InvalidParameter;
            img.PaletteFlags = Marshal.ReadInt32(palette);
            img.Palette = new int[count];
            Marshal.Copy(palette + 8, img.Palette, 0, count);
            return Ok;
        }

        // Frames

        internal static int GdipImageGetFrameDimensionsCount(HandleRef image, out int count)
        {
            var img = Image(image);
            count = img == null ? 0 : 1;
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipImageGetFrameDimensionsList(HandleRef image, Guid* dimensionIDs, int count)
        {
            var img = Image(image);
            if (img == null || count < 1) return InvalidParameter;
            dimensionIDs[0] = img.FrameDimension == Guid.Empty ? FrameDimension.Page.Guid : img.FrameDimension;
            return Ok;
        }

        internal static int GdipImageGetFrameCount(HandleRef image, ref Guid dimensionID, out int count)
        {
            var img = Image(image);
            count = 0;
            if (img == null) return InvalidParameter;
            var dimension = img.FrameDimension == Guid.Empty ? FrameDimension.Page.Guid : img.FrameDimension;
            if (dimensionID != dimension) return InvalidParameter;
            count = img.FrameCount;
            return Ok;
        }

        internal static int GdipImageSelectActiveFrame(HandleRef image, ref Guid dimensionID, int frameIndex)
        {
            var img = Image(image);
            if (img == null || frameIndex < 0 || frameIndex >= img.FrameCount) return InvalidParameter;
            img.SelectFrame(frameIndex);
            return Ok;
        }

        // Property items: PropertyItem { int id; int len; short type; void* value; }, values after the array.

        internal static int GdipGetPropertyCount(HandleRef image, out uint numOfProperty)
        {
            var img = Image(image);
            numOfProperty = (uint)(img?.Properties.Count ?? 0);
            return img == null ? InvalidParameter : Ok;
        }

        internal static int GdipGetPropertyIdList(HandleRef image, uint numOfProperty, int* list)
        {
            var img = Image(image);
            if (img == null || numOfProperty != img.Properties.Count) return InvalidParameter;
            for (int i = 0; i < img.Properties.Count; i++) list[i] = img.Properties[i].Id;
            return Ok;
        }

        internal static int GdipGetPropertyItemSize(HandleRef image, int propid, out uint size)
        {
            size = 0;
            var img = Image(image);
            if (img == null) return InvalidParameter;
            var p = img.Properties.FirstOrDefault(x => x.Id == propid);
            if (p == null) return PropertyNotFound;
            size = (uint)(sizeof(PropertyItemInternal) + (p.Value?.Length ?? 0));
            return Ok;
        }

        internal static int GdipGetPropertyItem(HandleRef image, int propid, uint propSize, PropertyItemInternal* buffer)
        {
            var img = Image(image);
            if (img == null || buffer == null) return InvalidParameter;
            var p = img.Properties.FirstOrDefault(x => x.Id == propid);
            if (p == null) return PropertyNotFound;
            int length = p.Value?.Length ?? 0;
            if (propSize < sizeof(PropertyItemInternal) + length) return InvalidParameter;
            buffer->id = p.Id;
            buffer->len = length;
            buffer->type = p.Type;
            buffer->value = (byte*)(buffer + 1);
            if (length > 0) Marshal.Copy(p.Value, 0, (IntPtr)buffer->value, length);
            return Ok;
        }

        internal static int GdipGetPropertySize(HandleRef image, out uint totalBufferSize, out uint numProperties)
        {
            var img = Image(image);
            totalBufferSize = numProperties = 0;
            if (img == null) return InvalidParameter;
            numProperties = (uint)img.Properties.Count;
            totalBufferSize = (uint)(img.Properties.Count * sizeof(PropertyItemInternal) + img.Properties.Sum(p => p.Value?.Length ?? 0));
            return Ok;
        }

        internal static int GdipGetAllPropertyItems(HandleRef image, uint totalBufferSize, uint numProperties, PropertyItemInternal* allItems)
        {
            var img = Image(image);
            if (img == null || allItems == null || numProperties != img.Properties.Count) return InvalidParameter;
            byte* values = (byte*)(allItems + numProperties);
            for (int i = 0; i < img.Properties.Count; i++)
            {
                var p = img.Properties[i];
                int length = p.Value?.Length ?? 0;
                allItems[i].id = p.Id;
                allItems[i].len = length;
                allItems[i].type = p.Type;
                allItems[i].value = values;
                if (length > 0) Marshal.Copy(p.Value, 0, (IntPtr)values, length);
                values += length;
            }
            return Ok;
        }

        internal static int GdipSetPropertyItem(HandleRef image, PropertyItemInternal* item)
        {
            var img = Image(image);
            if (img == null || item == null) return InvalidParameter;
            var value = new byte[Math.Max(0, item->len)];
            if (item->len > 0 && item->value != null) Marshal.Copy((IntPtr)item->value, value, 0, item->len);
            img.Properties.RemoveAll(p => p.Id == item->id);
            img.Properties.Add(new GpProperty { Id = item->id, Type = item->type, Value = value });
            return Ok;
        }

        internal static int GdipRemovePropertyItem(HandleRef image, int propid)
        {
            var img = Image(image);
            if (img == null) return InvalidParameter;
            return img.Properties.RemoveAll(p => p.Id == propid) > 0 ? Ok : PropertyNotFound;
        }

        // Bitmaps

        internal static int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap)
        {
            bitmap = IntPtr.Zero;
            var pf = (PixelFormat)format;
            if (width <= 0 || height <= 0 || !GpImage.IsValidFormat(pf)) return InvalidParameter;
            if (scan0 != IntPtr.Zero && stride == 0) return InvalidParameter;
            GpImage image;
            try
            {
                image = GpImage.Create(width, height, pf);
            }
            catch (Exception)
            {
                return OutOfMemory;
            }
            if (scan0 != IntPtr.Zero) image.WritePixels(new Rectangle(0, 0, width, height), pf, (byte*)scan0, stride);
            bitmap = NewHandle(image);
            return Ok;
        }

        internal static int GdipCreateBitmapFromGraphics(int width, int height, HandleRef graphics, out IntPtr bitmap)
        {
            int status = GdipCreateBitmapFromScan0(width, height, 0, (int)PixelFormat.Format32bppPArgb, IntPtr.Zero, out bitmap);
            var g = Get<GpGraphics>(graphics);
            if (status == Ok && g != null)
            {
                var img = Image(bitmap);
                img.DpiX = g.DpiX;
                img.DpiY = g.DpiY;
            }
            return status;
        }

        internal static int GdipCloneBitmapAreaI(int x, int y, int width, int height, int format, HandleRef srcbitmap, out IntPtr dstbitmap)
            => GdipCloneBitmapArea(x, y, width, height, format, srcbitmap, out dstbitmap);

        internal static int GdipCloneBitmapArea(float x, float y, float width, float height, int format, HandleRef srcbitmap, out IntPtr dstbitmap)
        {
            dstbitmap = IntPtr.Zero;
            var src = Image(srcbitmap);
            if (src == null) return InvalidParameter;
            var rect = new Rectangle((int)x, (int)y, (int)width, (int)height);
            if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > src.Width || rect.Bottom > src.Height) return InvalidParameter;
            var pf = (PixelFormat)format;
            if (!GpImage.IsValidFormat(pf)) return InvalidParameter;
            var copy = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            src.Pixels.ExtractSubset(copy, new SKRectI(rect.X, rect.Y, rect.Right, rect.Bottom));
            var result = new GpImage(copy.Copy(), pf, ImageFormat.MemoryBmp.Guid) { DpiX = src.DpiX, DpiY = src.DpiY };
            copy.Dispose();
            if (GpImage.IsIndexed(pf)) result.Palette = (int[])(src.Palette ?? GpImage.DefaultPalette(pf)).Clone();
            if (!GpImage.HasAlpha(pf)) Opaque(result.Pixels);
            dstbitmap = NewHandle(result);
            return Ok;
        }

        static void Opaque(SKBitmap bitmap)
        {
            var span = bitmap.GetPixelSpan();
            var pixels = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), span.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            bitmap.NotifyPixelsChanged();
        }

        internal static int GdipBitmapGetPixel(HandleRef bitmap, int x, int y, out int argb)
        {
            argb = 0;
            var img = Image(bitmap);
            if (img == null || x < 0 || y < 0 || x >= img.Width || y >= img.Height) return InvalidParameter;
            argb = img.GetArgb(x, y);
            if (GpImage.IsIndexed(img.Format) && img.Palette != null) argb = img.Palette[img.NearestIndex(argb)];
            return Ok;
        }

        internal static int GdipBitmapSetPixel(HandleRef bitmap, int x, int y, int argb)
        {
            var img = Image(bitmap);
            if (img == null || x < 0 || y < 0 || x >= img.Width || y >= img.Height) return InvalidParameter;
            if (GpImage.IsIndexed(img.Format)) return InvalidParameter; // GDI+: SetPixel is not supported on indexed bitmaps
            img.SetArgb(x, y, argb);
            return Ok;
        }

        internal static int GdipBitmapSetResolution(HandleRef bitmap, float dpix, float dpiy)
        {
            var img = Image(bitmap);
            if (img == null || !(dpix > 0) || !(dpiy > 0)) return InvalidParameter; // NaN and non-positive; infinity is accepted
            img.DpiX = dpix;
            img.DpiY = dpiy;
            return Ok;
        }

        internal static int GdipBitmapLockBits(HandleRef bitmap, ref Rectangle rect, ImageLockMode flags, PixelFormat format, BitmapData lockedBitmapData)
        {
            var img = Image(bitmap);
            if (img == null || lockedBitmapData == null) return InvalidParameter;
            if (img.LockedBuffer != IntPtr.Zero) return WrongState;
            if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > img.Width || rect.Bottom > img.Height) return InvalidParameter;
            if (!GpImage.IsValidFormat(format)) return InvalidParameter;
            if (GpImage.IsIndexed(format) && format != img.Format) return InvalidParameter;
            int stride = GpImage.Stride(rect.Width, format);
            bool user = (flags & ImageLockMode.UserInputBuffer) != 0;
            IntPtr buffer = user ? lockedBitmapData.Scan0 : Marshal.AllocHGlobal(stride * rect.Height);
            if (user) stride = lockedBitmapData.Stride;
            if ((flags & ImageLockMode.ReadOnly) != 0 || !user) img.ReadPixels(rect, format, (byte*)buffer, stride);
            img.LockedBuffer = buffer;
            img.LockedOwnBuffer = !user;
            img.LockedRect = rect;
            img.LockedMode = flags;
            img.LockedFormat = format;
            img.LockedStride = stride;
            lockedBitmapData.Width = rect.Width;
            lockedBitmapData.Height = rect.Height;
            lockedBitmapData.Stride = stride;
            lockedBitmapData.PixelFormat = format;
            lockedBitmapData.Scan0 = buffer;
            return Ok;
        }

        internal static int GdipBitmapUnlockBits(HandleRef bitmap, BitmapData lockedBitmapData)
        {
            var img = Image(bitmap);
            if (img == null || lockedBitmapData == null) return InvalidParameter;
            if (img.LockedBuffer == IntPtr.Zero) return Win32Error; // GDI+: unlocking an unlocked bitmap (ExternalException)
            if ((img.LockedMode & ImageLockMode.WriteOnly) != 0)
                img.WritePixels(img.LockedRect, img.LockedFormat, (byte*)img.LockedBuffer, img.LockedStride);
            if (img.LockedOwnBuffer) Marshal.FreeHGlobal(img.LockedBuffer);
            img.LockedBuffer = IntPtr.Zero;
            return Ok;
        }

        internal static int GdipImageRotateFlip(HandleRef image, int rotateFlipType)
        {
            var img = Image(image);
            if (img == null || rotateFlipType < 0 || rotateFlipType > 7) return InvalidParameter;
            int rotation = rotateFlipType & 3;           // 0, 90, 180, 270 clockwise
            bool flipX = (rotateFlipType & 4) != 0;      // then flip horizontally
            int w = img.Width, h = img.Height;
            bool swap = rotation == 1 || rotation == 3;
            var result = new SKBitmap(new SKImageInfo(swap ? h : w, swap ? w : h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(result))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(result.Width / 2f, result.Height / 2f);
                if (flipX) canvas.Scale(-1, 1);
                canvas.RotateDegrees(rotation * 90);
                canvas.Translate(-w / 2f, -h / 2f);
                using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                canvas.DrawBitmap(img.Pixels, 0, 0, paint);
            }
            img.Pixels.Dispose();
            img.Pixels = result;
            if (swap) (img.DpiX, img.DpiY) = (img.DpiY, img.DpiX);
            return Ok;
        }

        internal static int GdipGetImageThumbnail(IntPtr image, uint width, uint height, out IntPtr thumbImage, IntPtr callback, IntPtr callBackData)
        {
            thumbImage = IntPtr.Zero;
            var img = Image(image);
            if (img == null) return InvalidParameter;
            if (width == 0) width = 120;
            if (height == 0) height = 120;
            var thumb = GpImage.Create((int)width, (int)height, PixelFormat.Format32bppPArgb);
            using (var canvas = new SKCanvas(thumb.Pixels))
            using (var paint = new SKPaint { IsAntialias = true })
                canvas.DrawImage(SKImage.FromBitmap(img.Pixels), new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            thumbImage = NewHandle(thumb);
            return Ok;
        }
    }
    }
}
