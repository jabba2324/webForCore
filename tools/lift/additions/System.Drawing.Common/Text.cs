// webForCore: GDI+ fonts, string formats and text layout on SkiaSharp (see SkiaGdiPlus.cs).
// Licensed under the MIT license (as System.Drawing.Common).
#nullable disable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace System.Drawing
{
    // A font family. GDI+ on Windows always has the Windows core fonts; where one is not installed, the family resolves to
    // an installed metric-compatible font (Liberation for Arial, Times New Roman and Courier New, Carlito for Calibri...)
    // and keeps the requested name, as libgdiplus did through fontconfig. A family that resolves to nothing is not found.
    internal sealed class GpFontFamily
    {
        public string Name;
        public SKTypeface Typeface;     // regular
        public SKFontManager Manager;   // the family's source (installed or a private collection)
        readonly ConcurrentDictionary<FontStyle, SKTypeface> styles = new ConcurrentDictionary<FontStyle, SKTypeface>();

        public GpFontFamily Clone() => this; // immutable

        public SKTypeface ForStyle(FontStyle style)
        {
            style &= FontStyle.Bold | FontStyle.Italic;
            if (style == FontStyle.Regular) return Typeface;
            return styles.GetOrAdd(style, s =>
            {
                var skStyle = new SKFontStyle((s & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                    (s & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                var t = Manager?.MatchFamily(Typeface.FamilyName, skStyle);
                return t != null && string.Equals(t.FamilyName, Typeface.FamilyName, StringComparison.OrdinalIgnoreCase) ? t : Typeface;
            });
        }

        // Design-unit metrics (GetEmHeight, GetCellAscent, ...).
        public (int Em, int Ascent, int Descent, int LineSpacing) Metrics(FontStyle style)
        {
            var typeface = ForStyle(style);
            int em = typeface.UnitsPerEm;
            if (em <= 0) em = 2048;
            using var font = new SKFont(typeface, em);
            var m = font.Metrics;
            int ascent = (int)Math.Round(-m.Ascent), descent = (int)Math.Round(m.Descent);
            int spacing = (int)Math.Round(-m.Ascent + m.Descent + m.Leading);
            return (em, ascent, descent, Math.Max(spacing, ascent + descent));
        }

        static readonly Dictionary<string, string[]> Substitutes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = new[] { "Liberation Sans", "Arimo", "Nimbus Sans", "DejaVu Sans" },
            ["Helvetica"] = new[] { "Liberation Sans", "Nimbus Sans", "DejaVu Sans" },
            ["Microsoft Sans Serif"] = new[] { "Liberation Sans", "DejaVu Sans", "Noto Sans" },
            ["Tahoma"] = new[] { "DejaVu Sans", "Liberation Sans", "Noto Sans" },
            ["Verdana"] = new[] { "DejaVu Sans", "Liberation Sans", "Noto Sans" },
            ["Segoe UI"] = new[] { "Noto Sans", "DejaVu Sans", "Liberation Sans" },
            ["Calibri"] = new[] { "Carlito", "Liberation Sans" },
            ["Cambria"] = new[] { "Caladea", "Liberation Serif" },
            ["Times New Roman"] = new[] { "Liberation Serif", "Tinos", "Nimbus Roman", "DejaVu Serif" },
            ["Times"] = new[] { "Liberation Serif", "Nimbus Roman", "DejaVu Serif" },
            ["Georgia"] = new[] { "Liberation Serif", "DejaVu Serif" },
            ["Courier New"] = new[] { "Liberation Mono", "Cousine", "Nimbus Mono PS", "DejaVu Sans Mono" },
            ["Courier"] = new[] { "Liberation Mono", "Nimbus Mono PS", "DejaVu Sans Mono" },
            ["Consolas"] = new[] { "Liberation Mono", "DejaVu Sans Mono", "Noto Sans Mono" },
            ["Lucida Console"] = new[] { "Liberation Mono", "DejaVu Sans Mono" },
        };

        // The Windows core families that resolve here (InstalledFontCollection lists them, as on Windows).
        public static IEnumerable<GpFontFamily> SubstitutedFamilies => Substitutes.Keys.Select(Find).Where(f => f != null);

        static readonly ConcurrentDictionary<string, GpFontFamily> Installed = new ConcurrentDictionary<string, GpFontFamily>(StringComparer.OrdinalIgnoreCase);

        static SKTypeface Exact(SKFontManager manager, string family)
        {
            var t = manager.MatchFamily(family);
            return t != null && string.Equals(t.FamilyName, family, StringComparison.OrdinalIgnoreCase) ? t : null;
        }

        public static GpFontFamily Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Installed.GetOrAdd(name, n =>
            {
                var manager = SKFontManager.Default;
                var typeface = Exact(manager, n);
                if (typeface == null && Substitutes.TryGetValue(n, out var candidates))
                    typeface = candidates.Select(c => Exact(manager, c)).FirstOrDefault(t => t != null);
                if (typeface == null) return null;
                return new GpFontFamily { Name = typeface.FamilyName.Equals(n, StringComparison.OrdinalIgnoreCase) ? typeface.FamilyName : n, Typeface = typeface, Manager = manager };
            }) ?? Remove(name);
        }

        static GpFontFamily Remove(string name)
        {
            Installed.TryRemove(name, out _);
            return null;
        }

        // GDI+'s generic families are Windows fonts.
        public static GpFontFamily Generic(string windowsName, string fontconfigName)
            => Find(windowsName) ?? new GpFontFamily { Name = windowsName, Typeface = SKFontManager.Default.MatchFamily(fontconfigName) ?? SKTypeface.Default, Manager = SKFontManager.Default };
    }

    internal sealed class GpFontCollection
    {
        public bool IsPrivate;
        public List<GpFontFamily> Families = new List<GpFontFamily>();
    }

    internal sealed class GpFont
    {
        public GpFontFamily Family;
        public float Size;
        public FontStyle Style;
        public GraphicsUnit Unit;
        public GpFont Clone() => (GpFont)MemberwiseClone();
        public SKTypeface Typeface => Family.ForStyle(Style);

        // The em size in pixels at a resolution.
        public float Pixels(float dpi) => Unit switch
        {
            GraphicsUnit.World or GraphicsUnit.Pixel or GraphicsUnit.Display => Size,
            _ => Size * GpGraphics.UnitScale(Unit, dpi),
        };

        // A Skia font of a given pixel size, with synthetic bold/italic when the family lacks the style.
        public SKFont ToSkia(float pixels)
        {
            var typeface = Typeface;
            var font = new SKFont(typeface, pixels) { Subpixel = true, Edging = SKFontEdging.Antialias };
            if ((Style & FontStyle.Bold) != 0 && !typeface.IsBold) font.Embolden = true;
            if ((Style & FontStyle.Italic) != 0 && !typeface.IsItalic) font.SkewX = -0.2f;
            return font;
        }

        // Line spacing in pixels.
        public float Height(float dpi)
        {
            var m = Family.Metrics(Style);
            return Pixels(dpi) * m.LineSpacing / m.Em;
        }
    }

    internal sealed class GpStringFormat
    {
        public StringFormatFlags Flags;
        public StringAlignment Alignment, LineAlignment;
        public HotkeyPrefix HotkeyPrefix;
        public StringTrimming Trimming = StringTrimming.Character;
        public float FirstTabOffset;
        public float[] TabStops = Array.Empty<float>();
        public CharacterRange[] Ranges = Array.Empty<CharacterRange>();
        public int DigitLanguage;
        public StringDigitSubstitute DigitSubstitute;
        public bool Typographic;
        public GpStringFormat Clone()
        {
            var c = (GpStringFormat)MemberwiseClone();
            c.TabStops = (float[])TabStops.Clone();
            c.Ranges = (CharacterRange[])Ranges.Clone();
            return c;
        }
    }

    // GDI+-style layout of a string in a rectangle. Coordinates are in the units the font size is given in (world).
    internal sealed class TextLayout
    {
        internal sealed class Line
        {
            public int Start, Length;      // in the displayed text
            public string Text;
            public float X, Y, Width;
        }

        public readonly List<Line> Lines = new List<Line>();
        public string Display;             // text after hotkey prefix processing
        public int[] SourceIndex;          // display index -> source index
        public int Underline = -1;         // hotkey underline (display index)
        public float LineHeight, Ascent, Padding;
        public RectangleF Bounds;
        public int CodepointsFitted;
        public SKFont Font;

        public static TextLayout Create(string text, GpFont font, float size, RectangleF rect, GpStringFormat format)
        {
            format ??= new GpStringFormat();
            var layout = new TextLayout();
            var skFont = font.ToSkia(size);
            layout.Font = skFont;
            var fm = font.Family.Metrics(font.Style);
            layout.LineHeight = size * fm.LineSpacing / fm.Em;
            layout.Ascent = size * fm.Ascent / fm.Em;
            layout.Padding = format.Typographic ? 0 : size / 6;

            // Hotkey prefix: '&' marks the next character (Show underlines it, Hide drops the marker).
            var display = new StringBuilder();
            var map = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (format.HotkeyPrefix != HotkeyPrefix.None && c == '&' && i + 1 < text.Length)
                {
                    i++;
                    if (text[i] != '&' && format.HotkeyPrefix == HotkeyPrefix.Show) layout.Underline = display.Length;
                    c = text[i];
                }
                display.Append(c);
                map.Add(i);
            }
            layout.Display = display.ToString();
            layout.SourceIndex = map.ToArray();

            bool noWrap = (format.Flags & StringFormatFlags.NoWrap) != 0 || rect.Width <= 0;
            float available = rect.Width > 0 ? Math.Max(0, rect.Width - 2 * layout.Padding) : float.MaxValue;
            bool trailing = (format.Flags & StringFormatFlags.MeasureTrailingSpaces) != 0;
            float Measure(string s) => s.Length == 0 ? 0 : skFont.MeasureText(Expand(s, format, skFont));

            // Break into lines: at newlines, then (unless NoWrap) at spaces, or inside words that do not fit.
            string all = layout.Display;
            int pos = 0;
            var lines = new List<(int Start, int Length)>();
            while (pos <= all.Length)
            {
                int nl = all.IndexOf('\n', pos);
                int end = nl < 0 ? all.Length : nl;
                int segEnd = end > pos && all[end - 1] == '\r' ? end - 1 : end;
                if (noWrap)
                {
                    lines.Add((pos, segEnd - pos));
                }
                else
                {
                    int s = pos;
                    while (true)
                    {
                        if (Measure(all.Substring(s, segEnd - s).TrimEnd()) <= available || s >= segEnd)
                        {
                            lines.Add((s, segEnd - s));
                            break;
                        }
                        // the longest prefix ending at a word boundary that fits
                        int fit = s, lastSpace = -1;
                        for (int i = s + 1; i <= segEnd; i++)
                        {
                            if (Measure(all.Substring(s, i - s).TrimEnd()) > available) break;
                            fit = i;
                            if (i < segEnd && char.IsWhiteSpace(all[i])) lastSpace = i;
                        }
                        int breakAt = lastSpace > s ? lastSpace : Math.Max(fit, s + 1);
                        lines.Add((s, breakAt - s));
                        s = breakAt;
                        while (s < segEnd && all[s] == ' ') s++;
                        if (s >= segEnd) break;
                    }
                }
                if (nl < 0) break;
                pos = nl + 1;
            }

            // Lines that fit vertically (LineLimit: whole lines only).
            bool lineLimit = (format.Flags & StringFormatFlags.LineLimit) != 0;
            int maxLines = lines.Count;
            if (rect.Height > 0)
            {
                float fitLines = rect.Height / layout.LineHeight;
                maxLines = Math.Max(lineLimit ? (int)Math.Floor(fitLines + 1e-4f) : (int)Math.Ceiling(fitLines - 1e-4f), lineLimit ? 0 : 1);
                maxLines = Math.Min(maxLines, lines.Count);
            }
            bool truncated = maxLines < lines.Count;

            float maxWidth = 0;
            for (int i = 0; i < maxLines; i++)
            {
                var (start, length) = lines[i];
                string lineText = all.Substring(start, length);
                if (!trailing) lineText = lineText.TrimEnd(' ', '\r');
                if (truncated && i == maxLines - 1 && format.Trimming != StringTrimming.None)
                    lineText = Trim(lineText, all.Substring(start), format.Trimming, available, Measure);
                float width = Measure(lineText);
                maxWidth = Math.Max(maxWidth, width);
                layout.Lines.Add(new Line { Start = start, Length = Math.Min(length, lineText.Length), Text = lineText, Width = width });
            }
            layout.CodepointsFitted = layout.Lines.Count == 0 ? 0 : Math.Min(text.Length, layout.SourceIndex.ElementAtOrDefault(Math.Max(0, layout.Lines[^1].Start + layout.Lines[^1].Length - 1)) + 1);
            if (!truncated) layout.CodepointsFitted = text.Length;

            // Positions: alignment within the rectangle (or around the point when it has no size).
            float totalHeight = layout.Lines.Count * layout.LineHeight;
            float top = rect.Y;
            if (rect.Height > 0)
            {
                if (format.LineAlignment == StringAlignment.Center) top += (rect.Height - totalHeight) / 2;
                else if (format.LineAlignment == StringAlignment.Far) top += rect.Height - totalHeight;
            }
            else
            {
                if (format.LineAlignment == StringAlignment.Center) top -= totalHeight / 2;
                else if (format.LineAlignment == StringAlignment.Far) top -= totalHeight;
            }
            bool rtl = (format.Flags & StringFormatFlags.DirectionRightToLeft) != 0;
            var alignment = format.Alignment;
            if (rtl) alignment = alignment == StringAlignment.Near ? StringAlignment.Far : alignment == StringAlignment.Far ? StringAlignment.Near : alignment;
            for (int i = 0; i < layout.Lines.Count; i++)
            {
                var line = layout.Lines[i];
                line.Y = top + i * layout.LineHeight;
                if (rect.Width > 0)
                {
                    line.X = alignment switch
                    {
                        StringAlignment.Center => rect.X + (rect.Width - line.Width) / 2,
                        StringAlignment.Far => rect.Right - layout.Padding - line.Width,
                        _ => rect.X + layout.Padding,
                    };
                }
                else
                {
                    line.X = alignment switch
                    {
                        StringAlignment.Center => rect.X - line.Width / 2,
                        StringAlignment.Far => rect.X - line.Width - layout.Padding,
                        _ => rect.X + layout.Padding,
                    };
                }
            }

            // The bounding box GDI+ reports: the lines plus the padding.
            if (layout.Lines.Count > 0)
            {
                float left = layout.Lines.Min(l => l.X) - layout.Padding;
                float width = maxWidth + 2 * layout.Padding;
                if (rect.Width > 0 && (format.Flags & StringFormatFlags.NoClip) == 0) width = Math.Min(width, rect.Width);
                float height = totalHeight;
                if (rect.Height > 0 && !lineLimit) height = Math.Min(height, rect.Height);
                layout.Bounds = new RectangleF(rect.Width > 0 ? Math.Max(left, rect.X) : left, Math.Max(top, rect.Height > 0 && format.LineAlignment == StringAlignment.Near ? rect.Y : top), width, height);
            }
            else
            {
                layout.Bounds = new RectangleF(rect.X, rect.Y, 0, 0);
            }
            return layout;
        }

        // Tabs: to the next tab stop (default: 4 average character widths).
        public static string Expand(string s, GpStringFormat format, SKFont font)
        {
            if (s.IndexOf('\t') < 0) return s;
            return s.Replace("\t", "    ");
        }

        static string Trim(string line, string rest, StringTrimming trimming, float available, Func<string, float> measure)
        {
            switch (trimming)
            {
                case StringTrimming.Word:
                    return line;
                case StringTrimming.EllipsisCharacter:
                case StringTrimming.EllipsisWord:
                case StringTrimming.EllipsisPath:
                {
                    string candidate = line;
                    if (measure(candidate + "...") <= available && candidate.Length < rest.Length) return candidate + "...";
                    while (candidate.Length > 0 && measure(candidate + "...") > available)
                    {
                        candidate = trimming == StringTrimming.EllipsisWord && candidate.LastIndexOf(' ') > 0
                            ? candidate.Substring(0, candidate.LastIndexOf(' '))
                            : candidate.Substring(0, candidate.Length - 1);
                    }
                    return candidate + "...";
                }
                default:
                    return line;
            }
        }

        public void Draw(SKCanvas canvas, SKPaint paint, GpFont font, float size)
        {
            foreach (var line in Lines)
            {
                if (line.Text.Length == 0) continue;
                float baseline = line.Y + Ascent;
                canvas.DrawText(line.Text, line.X, baseline, Font, paint);
                if ((font.Style & (FontStyle.Underline | FontStyle.Strikeout)) != 0)
                {
                    var m = Font.Metrics;
                    float thickness = Math.Max(m.UnderlineThickness ?? size / 14, 1);
                    using var linePaint = paint.Clone();
                    linePaint.Style = SKPaintStyle.Fill;
                    if ((font.Style & FontStyle.Underline) != 0)
                    {
                        float y = baseline + (m.UnderlinePosition ?? size / 10);
                        canvas.DrawRect(line.X, y, line.Width, thickness, linePaint);
                    }
                    if ((font.Style & FontStyle.Strikeout) != 0)
                    {
                        float y = baseline + (m.StrikeoutPosition ?? -size / 3);
                        canvas.DrawRect(line.X, y, line.Width, thickness, linePaint);
                    }
                }
                if (Underline >= line.Start && Underline < line.Start + line.Text.Length)
                {
                    float x0 = line.X + Font.MeasureText(line.Text.Substring(0, Underline - line.Start));
                    float x1 = x0 + Font.MeasureText(line.Text.Substring(Underline - line.Start, 1));
                    using var linePaint = paint.Clone();
                    canvas.DrawRect(x0, baseline + size / 10, x1 - x0, Math.Max(size / 14, 1), linePaint);
                }
            }
        }

        // The rectangles a range of source characters occupies, line by line.
        public List<RectangleF> RangeRects(int first, int length)
        {
            var result = new List<RectangleF>();
            foreach (var line in Lines)
            {
                int start = -1, end = -1;
                for (int i = 0; i < line.Text.Length; i++)
                {
                    int source = SourceIndex[line.Start + i];
                    if (source >= first && source < first + length)
                    {
                        if (start < 0) start = i;
                        end = i + 1;
                    }
                }
                if (start < 0) continue;
                float x0 = line.X + Font.MeasureText(line.Text.Substring(0, start));
                float x1 = line.X + Font.MeasureText(line.Text.Substring(0, end));
                result.Add(new RectangleF(x0, line.Y, x1 - x0, LineHeight));
            }
            return result;
        }
    }

    internal static partial class SafeNativeMethods
    {
    internal static unsafe partial class SkiaGdiPlus
    {
        // Families and collections

        static GpFontFamily Family(HandleRef family) => Get<GpFontFamily>(family);
        static GpFontFamily Family(IntPtr family) => Get<GpFontFamily>(family);

        internal static int GdipCreateFontFamilyFromName(string name, HandleRef fontCollection, out IntPtr FontFamily)
        {
            FontFamily = IntPtr.Zero;
            if (name == null) return InvalidParameter;
            var collection = Get<GpFontCollection>(fontCollection);
            GpFontFamily family = collection != null && collection.IsPrivate
                ? collection.Families.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                : GpFontFamily.Find(name);
            if (family == null) return FontFamilyNotFound;
            FontFamily = NewHandle(family);
            return Ok;
        }

        internal static int GdipGetGenericFontFamilySansSerif(out IntPtr fontfamily) { fontfamily = NewHandle(GpFontFamily.Generic("Microsoft Sans Serif", "sans-serif")); return Ok; }
        internal static int GdipGetGenericFontFamilySerif(out IntPtr fontfamily) { fontfamily = NewHandle(GpFontFamily.Generic("Times New Roman", "serif")); return Ok; }
        internal static int GdipGetGenericFontFamilyMonospace(out IntPtr fontfamily) { fontfamily = NewHandle(GpFontFamily.Generic("Courier New", "monospace")); return Ok; }
        internal static int GdipCloneFontFamily(IntPtr fontfamily, out IntPtr clonefontfamily) { clonefontfamily = IntPtr.Zero; var f = Family(fontfamily); if (f == null) return InvalidParameter; clonefontfamily = NewHandle(f); return Ok; }
        internal static int GdipDeleteFontFamily(HandleRef fontFamily) { if (fontFamily.Handle == IntPtr.Zero) return InvalidParameter; GCHandle.FromIntPtr(fontFamily.Handle).Free(); return Ok; }

        internal static int GdipGetFamilyName(HandleRef family, char* name, int language)
        {
            var f = Family(family);
            if (f == null || name == null) return InvalidParameter;
            var n = f.Name.Length > 31 ? f.Name.Substring(0, 31) : f.Name; // LF_FACESIZE
            for (int i = 0; i < n.Length; i++) name[i] = n[i];
            name[n.Length] = '\0';
            return Ok;
        }

        internal static int GdipIsStyleAvailable(HandleRef family, FontStyle style, out int isStyleAvailable) { var f = Family(family); isStyleAvailable = f == null ? 0 : 1; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetEmHeight(HandleRef family, FontStyle style, out int EmHeight) { var f = Family(family); EmHeight = f?.Metrics(style).Em ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetCellAscent(HandleRef family, FontStyle style, out int CellAscent) { var f = Family(family); CellAscent = f?.Metrics(style).Ascent ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetCellDescent(HandleRef family, FontStyle style, out int CellDescent) { var f = Family(family); CellDescent = f?.Metrics(style).Descent ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetLineSpacing(HandleRef family, FontStyle style, out int LineSpaceing) { var f = Family(family); LineSpaceing = f?.Metrics(style).LineSpacing ?? 0; return f == null ? InvalidParameter : Ok; }

        internal static int GdipNewInstalledFontCollection(out IntPtr fontCollection)
        {
            var collection = new GpFontCollection();
            var families = SKFontManager.Default.FontFamilies.Select(GpFontFamily.Find).Where(f => f != null)
                .Concat(GpFontFamily.SubstitutedFamilies)
                .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            collection.Families.AddRange(families);
            fontCollection = NewHandle(collection);
            return Ok;
        }

        internal static int GdipNewPrivateFontCollection(out IntPtr fontCollection) { fontCollection = NewHandle(new GpFontCollection { IsPrivate = true }); return Ok; }
        internal static int GdipDeletePrivateFontCollection(ref IntPtr fontCollection) { int status = Free(fontCollection); fontCollection = IntPtr.Zero; return status; }

        static int AddPrivate(HandleRef fontCollection, SKTypeface typeface)
        {
            var collection = Get<GpFontCollection>(fontCollection);
            if (collection == null) return InvalidParameter;
            if (typeface == null) return FileNotFound;
            if (!collection.Families.Any(f => string.Equals(f.Name, typeface.FamilyName, StringComparison.OrdinalIgnoreCase)))
                collection.Families.Add(new GpFontFamily { Name = typeface.FamilyName, Typeface = typeface });
            return Ok;
        }

        internal static int GdipPrivateAddFontFile(HandleRef fontCollection, string filename)
        {
            if (filename == null) return InvalidParameter;
            if (!File.Exists(filename)) return FileNotFound;
            var typeface = SKTypeface.FromFile(filename);
            return typeface == null ? Ok : AddPrivate(fontCollection, typeface); // GDI+ skips a file that is not a font
        }

        internal static int GdipPrivateAddMemoryFont(HandleRef fontCollection, IntPtr memory, int length)
        {
            if (memory == IntPtr.Zero || length <= 0) return InvalidParameter;
            var bytes = new byte[length];
            Marshal.Copy(memory, bytes, 0, length);
            return AddPrivate(fontCollection, SKTypeface.FromData(SKData.CreateCopy(bytes)));
        }

        internal static int GdipGetFontCollectionFamilyCount(HandleRef fontCollection, out int numFound) { var c = Get<GpFontCollection>(fontCollection); numFound = c?.Families.Count ?? 0; return c == null ? InvalidParameter : Ok; }

        internal static int GdipGetFontCollectionFamilyList(HandleRef fontCollection, int numSought, IntPtr[] gpfamilies, out int numFound)
        {
            numFound = 0;
            var c = Get<GpFontCollection>(fontCollection);
            if (c == null || gpfamilies == null) return InvalidParameter;
            numFound = Math.Min(numSought, c.Families.Count);
            for (int i = 0; i < numFound; i++) gpfamilies[i] = NewHandle(c.Families[i]);
            return Ok;
        }

        // Fonts

        static GpFont Font(HandleRef font) => Get<GpFont>(font);

        internal static int GdipCreateFont(HandleRef fontFamily, float emSize, FontStyle style, GraphicsUnit unit, out IntPtr font)
        {
            font = IntPtr.Zero;
            var f = Family(fontFamily);
            if (f == null || emSize <= 0 || unit == GraphicsUnit.Display || unit < 0 || unit > GraphicsUnit.Millimeter) return InvalidParameter;
            font = NewHandle(new GpFont { Family = f, Size = emSize, Style = style, Unit = unit });
            return Ok;
        }

        internal static int GdipCloneFont(HandleRef font, out IntPtr cloneFont) { cloneFont = IntPtr.Zero; var f = Font(font); if (f == null) return InvalidParameter; cloneFont = NewHandle(f.Clone()); return Ok; }
        internal static int GdipDeleteFont(HandleRef font) => Free(font);
        internal static int GdipGetFamily(HandleRef font, out IntPtr family) { family = IntPtr.Zero; var f = Font(font); if (f == null) return InvalidParameter; family = NewHandle(f.Family); return Ok; }
        internal static int GdipGetFontStyle(HandleRef font, out FontStyle style) { var f = Font(font); style = f?.Style ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetFontSize(HandleRef font, out float size) { var f = Font(font); size = f?.Size ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetFontUnit(HandleRef font, out GraphicsUnit unit) { var f = Font(font); unit = f?.Unit ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipGetFontHeightGivenDPI(HandleRef font, float dpi, out float size) { var f = Font(font); size = f?.Height(dpi) ?? 0; return f == null ? InvalidParameter : Ok; }

        // Line spacing in the Graphics' page units.
        internal static int GdipGetFontHeight(HandleRef font, HandleRef graphics, out float size)
        {
            size = 0;
            var f = Font(font);
            if (f == null) return InvalidParameter;
            var g = Get<GpGraphics>(graphics);
            if (g == null)
            {
                size = f.Height(96);
                return Ok;
            }
            size = f.Height(g.DpiY) / g.PageMatrix.M22;
            return Ok;
        }

        internal static int GdipGetLogFontW(HandleRef font, HandleRef graphics, ref Interop.User32.LOGFONT lf)
        {
            var f = Font(font);
            var g = Get<GpGraphics>(graphics);
            if (f == null || g == null) return InvalidParameter;
            float dpi = g.DpiY;
            lf.lfHeight = -(int)Math.Round(f.Pixels(dpi));
            lf.lfWidth = 0;
            lf.lfWeight = (f.Style & FontStyle.Bold) != 0 ? 700 : 400;
            lf.lfItalic = (byte)((f.Style & FontStyle.Italic) != 0 ? 1 : 0);
            lf.lfUnderline = (byte)((f.Style & FontStyle.Underline) != 0 ? 1 : 0);
            lf.lfStrikeOut = (byte)((f.Style & FontStyle.Strikeout) != 0 ? 1 : 0);
            lf.lfCharSet = 0; // GDI+ leaves ANSI_CHARSET; Font.ToLogFont then puts in the font's GdiCharSet
            var face = lf.lfFaceName;
            face.Clear();
            f.Family.Name.AsSpan(0, Math.Min(f.Family.Name.Length, face.Length - 1)).CopyTo(face);
            return Ok;
        }

        internal static int GdipCreateFontFromLogfont(IntPtr hdc, ref Interop.User32.LOGFONT lf, out IntPtr ptr)
        {
            ptr = IntPtr.Zero;
            var family = GpFontFamily.Find(new string(lf.lfFaceName).TrimEnd('\0')) ?? GpFontFamily.Generic("Microsoft Sans Serif", "sans-serif");
            var style = (lf.lfWeight >= 700 ? FontStyle.Bold : 0) | (lf.lfItalic != 0 ? FontStyle.Italic : 0) | (lf.lfUnderline != 0 ? FontStyle.Underline : 0) | (lf.lfStrikeOut != 0 ? FontStyle.Strikeout : 0);
            float size = Math.Abs(lf.lfHeight);
            if (size == 0) size = 12;
            ptr = NewHandle(new GpFont { Family = family, Size = size, Style = style, Unit = GraphicsUnit.World });
            return Ok;
        }

        // String formats

        static GpStringFormat Format(HandleRef format) => Get<GpStringFormat>(format);
        static int SetFormat(HandleRef format, Action<GpStringFormat> set) { var f = Format(format); if (f == null) return InvalidParameter; set(f); return Ok; }

        internal static int GdipCreateStringFormat(StringFormatFlags options, int language, out IntPtr format) { format = NewHandle(new GpStringFormat { Flags = options }); return Ok; }
        internal static int GdipStringFormatGetGenericDefault(out IntPtr format) { format = NewHandle(new GpStringFormat()); return Ok; }

        internal static int GdipStringFormatGetGenericTypographic(out IntPtr format)
        {
            format = NewHandle(new GpStringFormat { Typographic = true, Flags = StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip, Trimming = StringTrimming.None });
            return Ok;
        }

        internal static int GdipDeleteStringFormat(HandleRef format) => Free(format);
        internal static int GdipCloneStringFormat(HandleRef format, out IntPtr newFormat) { newFormat = IntPtr.Zero; var f = Format(format); if (f == null) return InvalidParameter; newFormat = NewHandle(f.Clone()); return Ok; }
        internal static int GdipSetStringFormatFlags(HandleRef format, StringFormatFlags options) => SetFormat(format, f => f.Flags = options);
        internal static int GdipGetStringFormatFlags(HandleRef format, out StringFormatFlags result) { var f = Format(format); result = f?.Flags ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatAlign(HandleRef format, StringAlignment align) => SetFormat(format, f => f.Alignment = align);
        internal static int GdipGetStringFormatAlign(HandleRef format, out StringAlignment align) { var f = Format(format); align = f?.Alignment ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatLineAlign(HandleRef format, StringAlignment align) => SetFormat(format, f => f.LineAlignment = align);
        internal static int GdipGetStringFormatLineAlign(HandleRef format, out StringAlignment align) { var f = Format(format); align = f?.LineAlignment ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatHotkeyPrefix(HandleRef format, HotkeyPrefix hotkeyPrefix) => SetFormat(format, f => f.HotkeyPrefix = hotkeyPrefix);
        internal static int GdipGetStringFormatHotkeyPrefix(HandleRef format, out HotkeyPrefix hotkeyPrefix) { var f = Format(format); hotkeyPrefix = f?.HotkeyPrefix ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatTabStops(HandleRef format, float firstTabOffset, int count, float[] tabStops) => count < 0 ? InvalidParameter : SetFormat(format, f => { f.FirstTabOffset = firstTabOffset; f.TabStops = (tabStops ?? Array.Empty<float>()).Take(count).ToArray(); });

        internal static int GdipGetStringFormatTabStops(HandleRef format, int count, out float firstTabOffset, float[] tabStops)
        {
            var f = Format(format);
            firstTabOffset = f?.FirstTabOffset ?? 0;
            if (f == null || tabStops == null) return InvalidParameter;
            Array.Copy(f.TabStops, tabStops, Math.Min(count, f.TabStops.Length));
            return Ok;
        }

        internal static int GdipGetStringFormatTabStopCount(HandleRef format, out int count) { var f = Format(format); count = f?.TabStops.Length ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatMeasurableCharacterRanges(HandleRef format, int rangeCount, CharacterRange[] range) => rangeCount > 32 ? ValueOverflow : SetFormat(format, f => f.Ranges = (range ?? Array.Empty<CharacterRange>()).Take(rangeCount).ToArray());
        internal static int GdipGetStringFormatMeasurableCharacterRangeCount(HandleRef format, out int count) { var f = Format(format); count = f?.Ranges.Length ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatTrimming(HandleRef format, StringTrimming trimming) => trimming < StringTrimming.None || trimming > StringTrimming.EllipsisPath ? InvalidParameter : SetFormat(format, f => f.Trimming = trimming);
        internal static int GdipGetStringFormatTrimming(HandleRef format, out StringTrimming trimming) { var f = Format(format); trimming = f?.Trimming ?? 0; return f == null ? InvalidParameter : Ok; }
        internal static int GdipSetStringFormatDigitSubstitution(HandleRef format, int langID, StringDigitSubstitute sds) => SetFormat(format, f => { f.DigitLanguage = langID; f.DigitSubstitute = sds; });
        internal static int GdipGetStringFormatDigitSubstitution(HandleRef format, out int langID, out StringDigitSubstitute sds) { var f = Format(format); langID = f?.DigitLanguage ?? 0; sds = f?.DigitSubstitute ?? 0; return f == null ? InvalidParameter : Ok; }

        // Text on a Graphics: the font's size in world units.
        static float WorldFontSize(GpGraphics g, GpFont font)
        {
            if (font.Unit == GraphicsUnit.World) return font.Size;
            float pixels = font.Pixels(g.DpiY);
            return pixels / g.PageMatrix.M22;
        }

        internal static int GdipDrawString(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, HandleRef brush)
        {
            var g = G(graphics);
            var f = Font(font);
            var b = Get<GpBrush>(brush);
            if (g == null || f == null || b == null || textString == null) return InvalidParameter;
            string text = length >= 0 && length < textString.Length ? textString.Substring(0, length) : textString;
            var format = Format(stringFormat);
            float size = WorldFontSize(g, f);
            var rect = layoutRect;
            var layout = TextLayout.Create(text, f, size, rect, format);
            using var paint = g.Fill(b);
            paint.IsAntialias = g.TextRenderingHint != TextRenderingHint.SingleBitPerPixel && g.TextRenderingHint != TextRenderingHint.SingleBitPerPixelGridFit;
            layout.Font.Edging = paint.IsAntialias ? SKFontEdging.Antialias : SKFontEdging.Alias;
            bool clip = rect.Width > 0 && rect.Height > 0 && (format == null || (format.Flags & StringFormatFlags.NoClip) == 0);
            g.Draw(c =>
            {
                if (clip) c.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));
                layout.Draw(c, paint, f, size);
            });
            layout.Font.Dispose();
            return Ok;
        }

        internal static int GdipMeasureString(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, ref RectangleF boundingBox, out int codepointsFitted, out int linesFilled)
        {
            codepointsFitted = linesFilled = 0;
            var g = G(graphics);
            var f = Font(font);
            if (g == null || f == null || textString == null) return InvalidParameter;
            string text = length >= 0 && length < textString.Length ? textString.Substring(0, length) : textString;
            var layout = TextLayout.Create(text, f, WorldFontSize(g, f), layoutRect, Format(stringFormat));
            boundingBox = text.Length == 0 ? new RectangleF(layoutRect.X, layoutRect.Y, 0, 0) : layout.Bounds;
            codepointsFitted = layout.CodepointsFitted;
            linesFilled = layout.Lines.Count;
            layout.Font.Dispose();
            return Ok;
        }

        internal static int GdipMeasureCharacterRanges(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, int characterCount, IntPtr[] region)
        {
            var g = G(graphics);
            var f = Font(font);
            var format = Format(stringFormat);
            if (g == null || f == null || textString == null || region == null) return InvalidParameter;
            string text = length >= 0 && length < textString.Length ? textString.Substring(0, length) : textString;
            var ranges = format?.Ranges ?? Array.Empty<CharacterRange>();
            // GDI+: a range outside the text is an invalid parameter.
            foreach (var range in ranges)
            {
                int end = range.First + range.Length;
                if (range.First < 0 || range.First > text.Length || end < 0 || end > text.Length) return InvalidParameter;
            }
            // A layout rectangle with a negative size measures to infinite regions (GDI+).
            if (layoutRect.Width < 0 || layoutRect.Height < 0)
            {
                for (int i = 0; i < Math.Min(characterCount, ranges.Length); i++) Get<GpRegion>(region[i])?.SetInfinite();
                return Ok;
            }
            var layout = TextLayout.Create(text, f, WorldFontSize(g, f), layoutRect, format);
            for (int i = 0; i < Math.Min(characterCount, ranges.Length); i++)
            {
                var r = Get<GpRegion>(region[i]);
                if (r == null) continue;
                r.SetEmpty();
                foreach (var rect in layout.RangeRects(ranges[i].First, ranges[i].Length))
                    r.Combine(GpRegion.FromRect(rect), CombineMode.Union);
            }
            layout.Font.Dispose();
            return Ok;
        }

        // Text as a path (GraphicsPath.AddString): emSize in world units.
        internal static int GdipAddPathString(IntPtr path, string s, int lenght, IntPtr family, int style, float emSize, ref RectangleF layoutRect, IntPtr format)
        {
            var p = Get<GpPath>(path);
            var fam = Family(family);
            if (p == null || fam == null || s == null) return InvalidParameter;
            string text = lenght >= 0 && lenght < s.Length ? s.Substring(0, lenght) : s;
            var font = new GpFont { Family = fam, Size = emSize, Style = (FontStyle)style, Unit = GraphicsUnit.World };
            var layout = TextLayout.Create(text, font, emSize, layoutRect, Get<GpStringFormat>(format));
            foreach (var line in layout.Lines)
            {
                if (line.Text.Length == 0) continue;
                using var glyphs = layout.Font.GetTextPath(line.Text, new SKPoint(line.X, line.Y + layout.Ascent));
                var gp = GpPath.FromSkia(glyphs, p.FillMode);
                p.AddPath(gp, false);
                p.StartFigure();
            }
            layout.Font.Dispose();
            return Ok;
        }

        internal static int GdipAddPathStringI(IntPtr path, string s, int lenght, IntPtr family, int style, float emSize, ref Rectangle layoutRect, IntPtr format)
        {
            RectangleF r = layoutRect;
            return GdipAddPathString(path, s, lenght, family, style, emSize, ref r, format);
        }
    }
    }
}
