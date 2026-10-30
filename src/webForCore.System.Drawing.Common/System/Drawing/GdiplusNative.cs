// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace System.Drawing
{
    // Raw function imports for gdiplus
    // Functions are loaded manually in order to accomodate different shared library names on Unix.
    internal static partial class SafeNativeMethods
    {
        internal static unsafe partial class Gdip
        {
            // Shared function imports (all platforms)
            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBeginContainer(HandleRef graphics, ref RectangleF dstRect, ref RectangleF srcRect, GraphicsUnit unit, out int state) => SkiaGdiPlus.GdipBeginContainer(graphics, ref dstRect, ref srcRect, unit, out state);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBeginContainer2(HandleRef graphics, out int state) => SkiaGdiPlus.GdipBeginContainer2(graphics, out state);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBeginContainerI(HandleRef graphics, ref Rectangle dstRect, ref Rectangle srcRect, GraphicsUnit unit, out int state) => SkiaGdiPlus.GdipBeginContainerI(graphics, ref dstRect, ref srcRect, unit, out state);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipEndContainer(HandleRef graphics, int state) => SkiaGdiPlus.GdipEndContainer(graphics, state);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateAdjustableArrowCap(float height, float width, bool isFilled, out IntPtr adjustableArrowCap) => SkiaGdiPlus.GdipCreateAdjustableArrowCap(height, width, isFilled, out adjustableArrowCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetAdjustableArrowCapHeight(HandleRef adjustableArrowCap, out float height) => SkiaGdiPlus.GdipGetAdjustableArrowCapHeight(adjustableArrowCap, out height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetAdjustableArrowCapHeight(HandleRef adjustableArrowCap, float height) => SkiaGdiPlus.GdipSetAdjustableArrowCapHeight(adjustableArrowCap, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetAdjustableArrowCapWidth(HandleRef adjustableArrowCap, float width) => SkiaGdiPlus.GdipSetAdjustableArrowCapWidth(adjustableArrowCap, width);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetAdjustableArrowCapWidth(HandleRef adjustableArrowCap, out float width) => SkiaGdiPlus.GdipGetAdjustableArrowCapWidth(adjustableArrowCap, out width);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetAdjustableArrowCapMiddleInset(HandleRef adjustableArrowCap, float middleInset) => SkiaGdiPlus.GdipSetAdjustableArrowCapMiddleInset(adjustableArrowCap, middleInset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetAdjustableArrowCapMiddleInset(HandleRef adjustableArrowCap, out float middleInset) => SkiaGdiPlus.GdipGetAdjustableArrowCapMiddleInset(adjustableArrowCap, out middleInset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetAdjustableArrowCapFillState(HandleRef adjustableArrowCap, bool fillState) => SkiaGdiPlus.GdipSetAdjustableArrowCapFillState(adjustableArrowCap, fillState);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetAdjustableArrowCapFillState(HandleRef adjustableArrowCap, out bool fillState) => SkiaGdiPlus.GdipGetAdjustableArrowCapFillState(adjustableArrowCap, out fillState);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapType(IntPtr customCap, out CustomLineCapType capType) => SkiaGdiPlus.GdipGetCustomLineCapType(customCap, out capType);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateCustomLineCap(HandleRef fillpath, HandleRef strokepath, LineCap baseCap, float baseInset, out IntPtr customCap) => SkiaGdiPlus.GdipCreateCustomLineCap(fillpath, strokepath, baseCap, baseInset, out customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteCustomLineCap(IntPtr customCap) => SkiaGdiPlus.GdipDeleteCustomLineCap(customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteCustomLineCap(HandleRef customCap) => SkiaGdiPlus.GdipDeleteCustomLineCap(customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneCustomLineCap(HandleRef customCap, out IntPtr clonedCap) => SkiaGdiPlus.GdipCloneCustomLineCap(customCap, out clonedCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCustomLineCapStrokeCaps(HandleRef customCap, LineCap startCap, LineCap endCap) => SkiaGdiPlus.GdipSetCustomLineCapStrokeCaps(customCap, startCap, endCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapStrokeCaps(HandleRef customCap, out LineCap startCap, out LineCap endCap) => SkiaGdiPlus.GdipGetCustomLineCapStrokeCaps(customCap, out startCap, out endCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCustomLineCapStrokeJoin(HandleRef customCap, LineJoin lineJoin) => SkiaGdiPlus.GdipSetCustomLineCapStrokeJoin(customCap, lineJoin);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapStrokeJoin(HandleRef customCap, out LineJoin lineJoin) => SkiaGdiPlus.GdipGetCustomLineCapStrokeJoin(customCap, out lineJoin);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCustomLineCapBaseCap(HandleRef customCap, LineCap baseCap) => SkiaGdiPlus.GdipSetCustomLineCapBaseCap(customCap, baseCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapBaseCap(HandleRef customCap, out LineCap baseCap) => SkiaGdiPlus.GdipGetCustomLineCapBaseCap(customCap, out baseCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCustomLineCapBaseInset(HandleRef customCap, float inset) => SkiaGdiPlus.GdipSetCustomLineCapBaseInset(customCap, inset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapBaseInset(HandleRef customCap, out float inset) => SkiaGdiPlus.GdipGetCustomLineCapBaseInset(customCap, out inset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCustomLineCapWidthScale(HandleRef customCap, float widthScale) => SkiaGdiPlus.GdipSetCustomLineCapWidthScale(customCap, widthScale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCustomLineCapWidthScale(HandleRef customCap, out float widthScale) => SkiaGdiPlus.GdipGetCustomLineCapWidthScale(customCap, out widthScale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePathIter(out IntPtr pathIter, HandleRef path) => SkiaGdiPlus.GdipCreatePathIter(out pathIter, path);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeletePathIter(HandleRef pathIter) => SkiaGdiPlus.GdipDeletePathIter(pathIter);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterNextSubpath(HandleRef pathIter, out int resultCount, out int startIndex, out int endIndex, out bool isClosed) => SkiaGdiPlus.GdipPathIterNextSubpath(pathIter, out resultCount, out startIndex, out endIndex, out isClosed);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterNextSubpathPath(HandleRef pathIter, out int resultCount, HandleRef path, out bool isClosed) => SkiaGdiPlus.GdipPathIterNextSubpathPath(pathIter, out resultCount, path, out isClosed);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterNextPathType(HandleRef pathIter, out int resultCount, out byte pathType, out int startIndex, out int endIndex) => SkiaGdiPlus.GdipPathIterNextPathType(pathIter, out resultCount, out pathType, out startIndex, out endIndex);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterNextMarker(HandleRef pathIter, out int resultCount, out int startIndex, out int endIndex) => SkiaGdiPlus.GdipPathIterNextMarker(pathIter, out resultCount, out startIndex, out endIndex);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterNextMarkerPath(HandleRef pathIter, out int resultCount, HandleRef path) => SkiaGdiPlus.GdipPathIterNextMarkerPath(pathIter, out resultCount, path);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterGetCount(HandleRef pathIter, out int count) => SkiaGdiPlus.GdipPathIterGetCount(pathIter, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterGetSubpathCount(HandleRef pathIter, out int count) => SkiaGdiPlus.GdipPathIterGetSubpathCount(pathIter, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterHasCurve(HandleRef pathIter, out bool hasCurve) => SkiaGdiPlus.GdipPathIterHasCurve(pathIter, out hasCurve);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterRewind(HandleRef pathIter) => SkiaGdiPlus.GdipPathIterRewind(pathIter);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterEnumerate(HandleRef pathIter, out int resultCount, PointF* points, byte* types, int count) => SkiaGdiPlus.GdipPathIterEnumerate(pathIter, out resultCount, points, types, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPathIterCopyData(HandleRef pathIter, out int resultCount, PointF* points, byte* types, int startIndex, int endIndex) => SkiaGdiPlus.GdipPathIterCopyData(pathIter, out resultCount, points, types, startIndex, endIndex);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateHatchBrush(int hatchstyle, int forecol, int backcol, out IntPtr brush) => SkiaGdiPlus.GdipCreateHatchBrush(hatchstyle, forecol, backcol, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetHatchStyle(HandleRef brush, out int hatchstyle) => SkiaGdiPlus.GdipGetHatchStyle(brush, out hatchstyle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetHatchForegroundColor(HandleRef brush, out int forecol) => SkiaGdiPlus.GdipGetHatchForegroundColor(brush, out forecol);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetHatchBackgroundColor(HandleRef brush, out int backcol) => SkiaGdiPlus.GdipGetHatchBackgroundColor(brush, out backcol);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneBrush(HandleRef brush, out IntPtr clonebrush) => SkiaGdiPlus.GdipCloneBrush(brush, out clonebrush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrush(ref PointF point1, ref PointF point2, int color1, int color2, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrush(ref point1, ref point2, color1, color2, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrushI(ref Point point1, ref Point point2, int color1, int color2, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrushI(ref point1, ref point2, color1, color2, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrushFromRect(ref RectangleF rect, int color1, int color2, LinearGradientMode lineGradientMode, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrushFromRect(ref rect, color1, color2, lineGradientMode, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrushFromRectI(ref Rectangle rect, int color1, int color2, LinearGradientMode lineGradientMode, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrushFromRectI(ref rect, color1, color2, lineGradientMode, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrushFromRectWithAngle(ref RectangleF rect, int color1, int color2, float angle, bool isAngleScaleable, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrushFromRectWithAngle(ref rect, color1, color2, angle, isAngleScaleable, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateLineBrushFromRectWithAngleI(ref Rectangle rect, int color1, int color2, float angle, bool isAngleScaleable, WrapMode wrapMode, out IntPtr lineGradient) => SkiaGdiPlus.GdipCreateLineBrushFromRectWithAngleI(ref rect, color1, color2, angle, isAngleScaleable, wrapMode, out lineGradient);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineColors(HandleRef brush, int color1, int color2) => SkiaGdiPlus.GdipSetLineColors(brush, color1, color2);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineColors(HandleRef brush, int[] colors) => SkiaGdiPlus.GdipGetLineColors(brush, colors);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineRect(HandleRef brush, out RectangleF gprectf) => SkiaGdiPlus.GdipGetLineRect(brush, out gprectf);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineGammaCorrection(HandleRef brush, out bool useGammaCorrection) => SkiaGdiPlus.GdipGetLineGammaCorrection(brush, out useGammaCorrection);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineGammaCorrection(HandleRef brush, bool useGammaCorrection) => SkiaGdiPlus.GdipSetLineGammaCorrection(brush, useGammaCorrection);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineSigmaBlend(HandleRef brush, float focus, float scale) => SkiaGdiPlus.GdipSetLineSigmaBlend(brush, focus, scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineLinearBlend(HandleRef brush, float focus, float scale) => SkiaGdiPlus.GdipSetLineLinearBlend(brush, focus, scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineBlendCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetLineBlendCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count) => SkiaGdiPlus.GdipGetLineBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count) => SkiaGdiPlus.GdipSetLineBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLinePresetBlendCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetLinePresetBlendCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLinePresetBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count) => SkiaGdiPlus.GdipGetLinePresetBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLinePresetBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count) => SkiaGdiPlus.GdipSetLinePresetBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineWrapMode(HandleRef brush, int wrapMode) => SkiaGdiPlus.GdipSetLineWrapMode(brush, wrapMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineWrapMode(HandleRef brush, out int wrapMode) => SkiaGdiPlus.GdipGetLineWrapMode(brush, out wrapMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetLineTransform(HandleRef brush) => SkiaGdiPlus.GdipResetLineTransform(brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyLineTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyLineTransform(brush, matrix, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipGetLineTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetLineTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipSetLineTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateLineTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => SkiaGdiPlus.GdipTranslateLineTransform(brush, dx, dy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScaleLineTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => SkiaGdiPlus.GdipScaleLineTransform(brush, sx, sy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotateLineTransform(HandleRef brush, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotateLineTransform(brush, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePathGradient(PointF* points, int count, WrapMode wrapMode, out IntPtr brush) => SkiaGdiPlus.GdipCreatePathGradient(points, count, wrapMode, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePathGradientI(Point* points, int count, WrapMode wrapMode, out IntPtr brush) => SkiaGdiPlus.GdipCreatePathGradientI(points, count, wrapMode, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePathGradientFromPath(HandleRef path, out IntPtr brush) => SkiaGdiPlus.GdipCreatePathGradientFromPath(path, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientCenterColor(HandleRef brush, out int color) => SkiaGdiPlus.GdipGetPathGradientCenterColor(brush, out color);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientCenterColor(HandleRef brush, int color) => SkiaGdiPlus.GdipSetPathGradientCenterColor(brush, color);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientSurroundColorsWithCount(HandleRef brush, int[] color, ref int count) => SkiaGdiPlus.GdipGetPathGradientSurroundColorsWithCount(brush, color, ref count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientSurroundColorsWithCount(HandleRef brush, int[] argb, ref int count) => SkiaGdiPlus.GdipSetPathGradientSurroundColorsWithCount(brush, argb, ref count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientCenterPoint(HandleRef brush, out PointF point) => SkiaGdiPlus.GdipGetPathGradientCenterPoint(brush, out point);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientCenterPoint(HandleRef brush, ref PointF point) => SkiaGdiPlus.GdipSetPathGradientCenterPoint(brush, ref point);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientRect(HandleRef brush, out RectangleF gprectf) => SkiaGdiPlus.GdipGetPathGradientRect(brush, out gprectf);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientPointCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetPathGradientPointCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientSurroundColorCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetPathGradientSurroundColorCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientBlendCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetPathGradientBlendCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientBlend(HandleRef brush, float[] blend, float[] positions, int count) => SkiaGdiPlus.GdipGetPathGradientBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientBlend(HandleRef brush, IntPtr blend, IntPtr positions, int count) => SkiaGdiPlus.GdipSetPathGradientBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientPresetBlendCount(HandleRef brush, out int count) => SkiaGdiPlus.GdipGetPathGradientPresetBlendCount(brush, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientPresetBlend(HandleRef brush, int[] blend, float[] positions, int count) => SkiaGdiPlus.GdipGetPathGradientPresetBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientPresetBlend(HandleRef brush, int[] blend, float[] positions, int count) => SkiaGdiPlus.GdipSetPathGradientPresetBlend(brush, blend, positions, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientSigmaBlend(HandleRef brush, float focus, float scale) => SkiaGdiPlus.GdipSetPathGradientSigmaBlend(brush, focus, scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientLinearBlend(HandleRef brush, float focus, float scale) => SkiaGdiPlus.GdipSetPathGradientLinearBlend(brush, focus, scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientWrapMode(HandleRef brush, int wrapmode) => SkiaGdiPlus.GdipSetPathGradientWrapMode(brush, wrapmode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientWrapMode(HandleRef brush, out int wrapmode) => SkiaGdiPlus.GdipGetPathGradientWrapMode(brush, out wrapmode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipSetPathGradientTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipGetPathGradientTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetPathGradientTransform(HandleRef brush) => SkiaGdiPlus.GdipResetPathGradientTransform(brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyPathGradientTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyPathGradientTransform(brush, matrix, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslatePathGradientTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => SkiaGdiPlus.GdipTranslatePathGradientTransform(brush, dx, dy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScalePathGradientTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => SkiaGdiPlus.GdipScalePathGradientTransform(brush, sx, sy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotatePathGradientTransform(HandleRef brush, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotatePathGradientTransform(brush, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPathGradientFocusScales(HandleRef brush, float[] xScale, float[] yScale) => SkiaGdiPlus.GdipGetPathGradientFocusScales(brush, xScale, yScale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPathGradientFocusScales(HandleRef brush, float xScale, float yScale) => SkiaGdiPlus.GdipSetPathGradientFocusScales(brush, xScale, yScale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateImageAttributes(out IntPtr imageattr) => SkiaGdiPlus.GdipCreateImageAttributes(out imageattr);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneImageAttributes(HandleRef imageattr, out IntPtr cloneImageattr) => SkiaGdiPlus.GdipCloneImageAttributes(imageattr, out cloneImageattr);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDisposeImageAttributes(HandleRef imageattr) => SkiaGdiPlus.GdipDisposeImageAttributes(imageattr);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesColorMatrix(HandleRef imageattr, ColorAdjustType type, bool enableFlag, ColorMatrix? colorMatrix, ColorMatrix? grayMatrix, ColorMatrixFlag flags) => SkiaGdiPlus.GdipSetImageAttributesColorMatrix(imageattr, type, enableFlag, colorMatrix, grayMatrix, flags);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesThreshold(HandleRef imageattr, ColorAdjustType type, bool enableFlag, float threshold) => SkiaGdiPlus.GdipSetImageAttributesThreshold(imageattr, type, enableFlag, threshold);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesGamma(HandleRef imageattr, ColorAdjustType type, bool enableFlag, float gamma) => SkiaGdiPlus.GdipSetImageAttributesGamma(imageattr, type, enableFlag, gamma);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesNoOp(HandleRef imageattr, ColorAdjustType type, bool enableFlag) => SkiaGdiPlus.GdipSetImageAttributesNoOp(imageattr, type, enableFlag);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesColorKeys(HandleRef imageattr, ColorAdjustType type, bool enableFlag, int colorLow, int colorHigh) => SkiaGdiPlus.GdipSetImageAttributesColorKeys(imageattr, type, enableFlag, colorLow, colorHigh);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesOutputChannel(HandleRef imageattr, ColorAdjustType type, bool enableFlag, ColorChannelFlag flags) => SkiaGdiPlus.GdipSetImageAttributesOutputChannel(imageattr, type, enableFlag, flags);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesOutputChannelColorProfile(HandleRef imageattr, ColorAdjustType type, bool enableFlag, string colorProfileFilename) => SkiaGdiPlus.GdipSetImageAttributesOutputChannelColorProfile(imageattr, type, enableFlag, colorProfileFilename);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesRemapTable(HandleRef imageattr, ColorAdjustType type, bool enableFlag, int mapSize, IntPtr map) => SkiaGdiPlus.GdipSetImageAttributesRemapTable(imageattr, type, enableFlag, mapSize, map);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetImageAttributesWrapMode(HandleRef imageattr, int wrapmode, int argb, bool clamp) => SkiaGdiPlus.GdipSetImageAttributesWrapMode(imageattr, wrapmode, argb, clamp);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageAttributesAdjustedPalette(HandleRef imageattr, IntPtr palette, ColorAdjustType type) => SkiaGdiPlus.GdipGetImageAttributesAdjustedPalette(imageattr, palette, type);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageDecodersSize(out int numDecoders, out int size) => SkiaGdiPlus.GdipGetImageDecodersSize(out numDecoders, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageDecoders(int numDecoders, int size, IntPtr decoders) => SkiaGdiPlus.GdipGetImageDecoders(numDecoders, size, decoders);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageEncodersSize(out int numEncoders, out int size) => SkiaGdiPlus.GdipGetImageEncodersSize(out numEncoders, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageEncoders(int numEncoders, int size, IntPtr encoders) => SkiaGdiPlus.GdipGetImageEncoders(numEncoders, size, encoders);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateSolidFill(int color, out IntPtr brush) => SkiaGdiPlus.GdipCreateSolidFill(color, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetSolidFillColor(HandleRef brush, int color) => SkiaGdiPlus.GdipSetSolidFillColor(brush, color);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetSolidFillColor(HandleRef brush, out int color) => SkiaGdiPlus.GdipGetSolidFillColor(brush, out color);


            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateTexture(HandleRef bitmap, int wrapmode, out IntPtr texture) => SkiaGdiPlus.GdipCreateTexture(bitmap, wrapmode, out texture);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateTexture2(HandleRef bitmap, int wrapmode, float x, float y, float width, float height, out IntPtr texture) => SkiaGdiPlus.GdipCreateTexture2(bitmap, wrapmode, x, y, width, height, out texture);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateTextureIA(HandleRef bitmap, HandleRef imageAttrib, float x, float y, float width, float height, out IntPtr texture) => SkiaGdiPlus.GdipCreateTextureIA(bitmap, imageAttrib, x, y, width, height, out texture);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateTexture2I(HandleRef bitmap, int wrapmode, int x, int y, int width, int height, out IntPtr texture) => SkiaGdiPlus.GdipCreateTexture2I(bitmap, wrapmode, x, y, width, height, out texture);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateTextureIAI(HandleRef bitmap, HandleRef imageAttrib, int x, int y, int width, int height, out IntPtr texture) => SkiaGdiPlus.GdipCreateTextureIAI(bitmap, imageAttrib, x, y, width, height, out texture);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetTextureTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipSetTextureTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetTextureTransform(HandleRef brush, HandleRef matrix) => SkiaGdiPlus.GdipGetTextureTransform(brush, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetTextureTransform(HandleRef brush) => SkiaGdiPlus.GdipResetTextureTransform(brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyTextureTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyTextureTransform(brush, matrix, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateTextureTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => SkiaGdiPlus.GdipTranslateTextureTransform(brush, dx, dy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScaleTextureTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => SkiaGdiPlus.GdipScaleTextureTransform(brush, sx, sy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotateTextureTransform(HandleRef brush, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotateTextureTransform(brush, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetTextureWrapMode(HandleRef brush, int wrapMode) => SkiaGdiPlus.GdipSetTextureWrapMode(brush, wrapMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetTextureWrapMode(HandleRef brush, out int wrapMode) => SkiaGdiPlus.GdipGetTextureWrapMode(brush, out wrapMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetTextureImage(HandleRef brush, out IntPtr image) => SkiaGdiPlus.GdipGetTextureImage(brush, out image);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontCollectionFamilyCount(HandleRef fontCollection, out int numFound) => SkiaGdiPlus.GdipGetFontCollectionFamilyCount(fontCollection, out numFound);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontCollectionFamilyList(HandleRef fontCollection, int numSought, IntPtr[] gpfamilies, out int numFound) => SkiaGdiPlus.GdipGetFontCollectionFamilyList(fontCollection, numSought, gpfamilies, out numFound);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneFontFamily(IntPtr fontfamily, out IntPtr clonefontfamily) => SkiaGdiPlus.GdipCloneFontFamily(fontfamily, out clonefontfamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateFontFamilyFromName(string name, HandleRef fontCollection, out IntPtr FontFamily) => SkiaGdiPlus.GdipCreateFontFamilyFromName(name, fontCollection, out FontFamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetGenericFontFamilySansSerif(out IntPtr fontfamily) => SkiaGdiPlus.GdipGetGenericFontFamilySansSerif(out fontfamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetGenericFontFamilySerif(out IntPtr fontfamily) => SkiaGdiPlus.GdipGetGenericFontFamilySerif(out fontfamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetGenericFontFamilyMonospace(out IntPtr fontfamily) => SkiaGdiPlus.GdipGetGenericFontFamilyMonospace(out fontfamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteFontFamily(HandleRef fontFamily) => SkiaGdiPlus.GdipDeleteFontFamily(fontFamily);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFamilyName(HandleRef family, char* name, int language) => SkiaGdiPlus.GdipGetFamilyName(family, name, language);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsStyleAvailable(HandleRef family, FontStyle style, out int isStyleAvailable) => SkiaGdiPlus.GdipIsStyleAvailable(family, style, out isStyleAvailable);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetEmHeight(HandleRef family, FontStyle style, out int EmHeight) => SkiaGdiPlus.GdipGetEmHeight(family, style, out EmHeight);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCellAscent(HandleRef family, FontStyle style, out int CellAscent) => SkiaGdiPlus.GdipGetCellAscent(family, style, out CellAscent);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCellDescent(HandleRef family, FontStyle style, out int CellDescent) => SkiaGdiPlus.GdipGetCellDescent(family, style, out CellDescent);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLineSpacing(HandleRef family, FontStyle style, out int LineSpaceing) => SkiaGdiPlus.GdipGetLineSpacing(family, style, out LineSpaceing);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipNewInstalledFontCollection(out IntPtr fontCollection) => SkiaGdiPlus.GdipNewInstalledFontCollection(out fontCollection);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipNewPrivateFontCollection(out IntPtr fontCollection) => SkiaGdiPlus.GdipNewPrivateFontCollection(out fontCollection);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeletePrivateFontCollection(ref IntPtr fontCollection) => SkiaGdiPlus.GdipDeletePrivateFontCollection(ref fontCollection);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPrivateAddFontFile(HandleRef fontCollection, string filename) => SkiaGdiPlus.GdipPrivateAddFontFile(fontCollection, filename);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPrivateAddMemoryFont(HandleRef fontCollection, IntPtr memory, int length) => SkiaGdiPlus.GdipPrivateAddMemoryFont(fontCollection, memory, length);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateFont(HandleRef fontFamily, float emSize, FontStyle style, GraphicsUnit unit, out IntPtr font) => SkiaGdiPlus.GdipCreateFont(fontFamily, emSize, style, unit, out font);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateFontFromDC(IntPtr hdc, ref IntPtr font) => SkiaGdiPlus.GdipCreateFontFromDC(hdc, ref font);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneFont(HandleRef font, out IntPtr cloneFont) => SkiaGdiPlus.GdipCloneFont(font, out cloneFont);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteFont(HandleRef font) => SkiaGdiPlus.GdipDeleteFont(font);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFamily(HandleRef font, out IntPtr family) => SkiaGdiPlus.GdipGetFamily(font, out family);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontStyle(HandleRef font, out FontStyle style) => SkiaGdiPlus.GdipGetFontStyle(font, out style);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontSize(HandleRef font, out float size) => SkiaGdiPlus.GdipGetFontSize(font, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontHeight(HandleRef font, HandleRef graphics, out float size) => SkiaGdiPlus.GdipGetFontHeight(font, graphics, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontHeightGivenDPI(HandleRef font, float dpi, out float size) => SkiaGdiPlus.GdipGetFontHeightGivenDPI(font, dpi, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetFontUnit(HandleRef font, out GraphicsUnit unit) => SkiaGdiPlus.GdipGetFontUnit(font, out unit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetLogFontW(HandleRef font, HandleRef graphics, ref Interop.User32.LOGFONT lf) => SkiaGdiPlus.GdipGetLogFontW(font, graphics, ref lf);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePen1(int argb, float width, int unit, out IntPtr pen) => SkiaGdiPlus.GdipCreatePen1(argb, width, unit, out pen);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreatePen2(HandleRef brush, float width, int unit, out IntPtr pen) => SkiaGdiPlus.GdipCreatePen2(brush, width, unit, out pen);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipClonePen(HandleRef pen, out IntPtr clonepen) => SkiaGdiPlus.GdipClonePen(pen, out clonepen);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeletePen(HandleRef Pen) => SkiaGdiPlus.GdipDeletePen(Pen);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenMode(HandleRef pen, PenAlignment penAlign) => SkiaGdiPlus.GdipSetPenMode(pen, penAlign);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenMode(HandleRef pen, out PenAlignment penAlign) => SkiaGdiPlus.GdipGetPenMode(pen, out penAlign);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenWidth(HandleRef pen, float width) => SkiaGdiPlus.GdipSetPenWidth(pen, width);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenWidth(HandleRef pen, float[] width) => SkiaGdiPlus.GdipGetPenWidth(pen, width);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenLineCap197819(HandleRef pen, int startCap, int endCap, int dashCap) => SkiaGdiPlus.GdipSetPenLineCap197819(pen, startCap, endCap, dashCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenStartCap(HandleRef pen, int startCap) => SkiaGdiPlus.GdipSetPenStartCap(pen, startCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenEndCap(HandleRef pen, int endCap) => SkiaGdiPlus.GdipSetPenEndCap(pen, endCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenStartCap(HandleRef pen, out int startCap) => SkiaGdiPlus.GdipGetPenStartCap(pen, out startCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenEndCap(HandleRef pen, out int endCap) => SkiaGdiPlus.GdipGetPenEndCap(pen, out endCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenDashCap197819(HandleRef pen, out int dashCap) => SkiaGdiPlus.GdipGetPenDashCap197819(pen, out dashCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenDashCap197819(HandleRef pen, int dashCap) => SkiaGdiPlus.GdipSetPenDashCap197819(pen, dashCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenLineJoin(HandleRef pen, int lineJoin) => SkiaGdiPlus.GdipSetPenLineJoin(pen, lineJoin);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenLineJoin(HandleRef pen, out int lineJoin) => SkiaGdiPlus.GdipGetPenLineJoin(pen, out lineJoin);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenCustomStartCap(HandleRef pen, HandleRef customCap) => SkiaGdiPlus.GdipSetPenCustomStartCap(pen, customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenCustomStartCap(HandleRef pen, out IntPtr customCap) => SkiaGdiPlus.GdipGetPenCustomStartCap(pen, out customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenCustomEndCap(HandleRef pen, HandleRef customCap) => SkiaGdiPlus.GdipSetPenCustomEndCap(pen, customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenCustomEndCap(HandleRef pen, out IntPtr customCap) => SkiaGdiPlus.GdipGetPenCustomEndCap(pen, out customCap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenMiterLimit(HandleRef pen, float miterLimit) => SkiaGdiPlus.GdipSetPenMiterLimit(pen, miterLimit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenMiterLimit(HandleRef pen, float[] miterLimit) => SkiaGdiPlus.GdipGetPenMiterLimit(pen, miterLimit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenTransform(HandleRef pen, HandleRef matrix) => SkiaGdiPlus.GdipSetPenTransform(pen, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenTransform(HandleRef pen, HandleRef matrix) => SkiaGdiPlus.GdipGetPenTransform(pen, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetPenTransform(HandleRef brush) => SkiaGdiPlus.GdipResetPenTransform(brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyPenTransform(HandleRef brush, HandleRef matrix, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyPenTransform(brush, matrix, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslatePenTransform(HandleRef brush, float dx, float dy, MatrixOrder order) => SkiaGdiPlus.GdipTranslatePenTransform(brush, dx, dy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScalePenTransform(HandleRef brush, float sx, float sy, MatrixOrder order) => SkiaGdiPlus.GdipScalePenTransform(brush, sx, sy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotatePenTransform(HandleRef brush, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotatePenTransform(brush, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenColor(HandleRef pen, int argb) => SkiaGdiPlus.GdipSetPenColor(pen, argb);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenColor(HandleRef pen, out int argb) => SkiaGdiPlus.GdipGetPenColor(pen, out argb);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenBrushFill(HandleRef pen, HandleRef brush) => SkiaGdiPlus.GdipSetPenBrushFill(pen, brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenBrushFill(HandleRef pen, out IntPtr brush) => SkiaGdiPlus.GdipGetPenBrushFill(pen, out brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenFillType(HandleRef pen, out int pentype) => SkiaGdiPlus.GdipGetPenFillType(pen, out pentype);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenDashStyle(HandleRef pen, out int dashstyle) => SkiaGdiPlus.GdipGetPenDashStyle(pen, out dashstyle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenDashStyle(HandleRef pen, int dashstyle) => SkiaGdiPlus.GdipSetPenDashStyle(pen, dashstyle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenDashArray(HandleRef pen, HandleRef memorydash, int count) => SkiaGdiPlus.GdipSetPenDashArray(pen, memorydash, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenDashOffset(HandleRef pen, float[] dashoffset) => SkiaGdiPlus.GdipGetPenDashOffset(pen, dashoffset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenDashOffset(HandleRef pen, float dashoffset) => SkiaGdiPlus.GdipSetPenDashOffset(pen, dashoffset);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenDashCount(HandleRef pen, out int dashcount) => SkiaGdiPlus.GdipGetPenDashCount(pen, out dashcount);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenDashArray(HandleRef pen, float[] memorydash, int count) => SkiaGdiPlus.GdipGetPenDashArray(pen, memorydash, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenCompoundCount(HandleRef pen, out int count) => SkiaGdiPlus.GdipGetPenCompoundCount(pen, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPenCompoundArray(HandleRef pen, float[] array, int count) => SkiaGdiPlus.GdipSetPenCompoundArray(pen, array, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPenCompoundArray(HandleRef pen, float[] array, int count) => SkiaGdiPlus.GdipGetPenCompoundArray(pen, array, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetWorldTransform(HandleRef graphics, HandleRef matrix) => SkiaGdiPlus.GdipSetWorldTransform(graphics, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetWorldTransform(HandleRef graphics) => SkiaGdiPlus.GdipResetWorldTransform(graphics);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyWorldTransform(HandleRef graphics, HandleRef matrix, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyWorldTransform(graphics, matrix, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateWorldTransform(HandleRef graphics, float dx, float dy, MatrixOrder order) => SkiaGdiPlus.GdipTranslateWorldTransform(graphics, dx, dy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScaleWorldTransform(HandleRef graphics, float sx, float sy, MatrixOrder order) => SkiaGdiPlus.GdipScaleWorldTransform(graphics, sx, sy, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotateWorldTransform(HandleRef graphics, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotateWorldTransform(graphics, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetWorldTransform(HandleRef graphics, HandleRef matrix) => SkiaGdiPlus.GdipGetWorldTransform(graphics, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCompositingMode(HandleRef graphics, CompositingMode compositingMode) => SkiaGdiPlus.GdipSetCompositingMode(graphics, compositingMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetTextRenderingHint(HandleRef graphics, TextRenderingHint textRenderingHint) => SkiaGdiPlus.GdipSetTextRenderingHint(graphics, textRenderingHint);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetTextContrast(HandleRef graphics, int textContrast) => SkiaGdiPlus.GdipSetTextContrast(graphics, textContrast);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetInterpolationMode(HandleRef graphics, InterpolationMode interpolationMode) => SkiaGdiPlus.GdipSetInterpolationMode(graphics, interpolationMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCompositingMode(HandleRef graphics, out CompositingMode compositingMode) => SkiaGdiPlus.GdipGetCompositingMode(graphics, out compositingMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetRenderingOrigin(HandleRef graphics, int x, int y) => SkiaGdiPlus.GdipSetRenderingOrigin(graphics, x, y);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRenderingOrigin(HandleRef graphics, out int x, out int y) => SkiaGdiPlus.GdipGetRenderingOrigin(graphics, out x, out y);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetCompositingQuality(HandleRef graphics, CompositingQuality quality) => SkiaGdiPlus.GdipSetCompositingQuality(graphics, quality);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetCompositingQuality(HandleRef graphics, out CompositingQuality quality) => SkiaGdiPlus.GdipGetCompositingQuality(graphics, out quality);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetSmoothingMode(HandleRef graphics, SmoothingMode smoothingMode) => SkiaGdiPlus.GdipSetSmoothingMode(graphics, smoothingMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetSmoothingMode(HandleRef graphics, out SmoothingMode smoothingMode) => SkiaGdiPlus.GdipGetSmoothingMode(graphics, out smoothingMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPixelOffsetMode(HandleRef graphics, PixelOffsetMode pixelOffsetMode) => SkiaGdiPlus.GdipSetPixelOffsetMode(graphics, pixelOffsetMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPixelOffsetMode(HandleRef graphics, out PixelOffsetMode pixelOffsetMode) => SkiaGdiPlus.GdipGetPixelOffsetMode(graphics, out pixelOffsetMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetTextRenderingHint(HandleRef graphics, out TextRenderingHint textRenderingHint) => SkiaGdiPlus.GdipGetTextRenderingHint(graphics, out textRenderingHint);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetTextContrast(HandleRef graphics, out int textContrast) => SkiaGdiPlus.GdipGetTextContrast(graphics, out textContrast);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetInterpolationMode(HandleRef graphics, out InterpolationMode interpolationMode) => SkiaGdiPlus.GdipGetInterpolationMode(graphics, out interpolationMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPageUnit(HandleRef graphics, out GraphicsUnit unit) => SkiaGdiPlus.GdipGetPageUnit(graphics, out unit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPageScale(HandleRef graphics, out float scale) => SkiaGdiPlus.GdipGetPageScale(graphics, out scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPageUnit(HandleRef graphics, GraphicsUnit unit) => SkiaGdiPlus.GdipSetPageUnit(graphics, unit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPageScale(HandleRef graphics, float scale) => SkiaGdiPlus.GdipSetPageScale(graphics, scale);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetDpiX(HandleRef graphics, out float dpi) => SkiaGdiPlus.GdipGetDpiX(graphics, out dpi);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetDpiY(HandleRef graphics, out float dpi) => SkiaGdiPlus.GdipGetDpiY(graphics, out dpi);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMatrix(out IntPtr matrix) => SkiaGdiPlus.GdipCreateMatrix(out matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMatrix2(float m11, float m12, float m21, float m22, float dx, float dy, out IntPtr matrix) => SkiaGdiPlus.GdipCreateMatrix2(m11, m12, m21, m22, dx, dy, out matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMatrix3(ref RectangleF rect, PointF* dstplg, out IntPtr matrix) => SkiaGdiPlus.GdipCreateMatrix3(ref rect, dstplg, out matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMatrix3I(ref Rectangle rect, Point* dstplg, out IntPtr matrix) => SkiaGdiPlus.GdipCreateMatrix3I(ref rect, dstplg, out matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneMatrix(HandleRef matrix, out IntPtr cloneMatrix) => SkiaGdiPlus.GdipCloneMatrix(matrix, out cloneMatrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteMatrix(HandleRef matrix) => SkiaGdiPlus.GdipDeleteMatrix(matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetMatrixElements(HandleRef matrix, float m11, float m12, float m21, float m22, float dx, float dy) => SkiaGdiPlus.GdipSetMatrixElements(matrix, m11, m12, m21, m22, dx, dy);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMultiplyMatrix(HandleRef matrix, HandleRef matrix2, MatrixOrder order) => SkiaGdiPlus.GdipMultiplyMatrix(matrix, matrix2, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateMatrix(HandleRef matrix, float offsetX, float offsetY, MatrixOrder order) => SkiaGdiPlus.GdipTranslateMatrix(matrix, offsetX, offsetY, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipScaleMatrix(HandleRef matrix, float scaleX, float scaleY, MatrixOrder order) => SkiaGdiPlus.GdipScaleMatrix(matrix, scaleX, scaleY, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRotateMatrix(HandleRef matrix, float angle, MatrixOrder order) => SkiaGdiPlus.GdipRotateMatrix(matrix, angle, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipShearMatrix(HandleRef matrix, float shearX, float shearY, MatrixOrder order) => SkiaGdiPlus.GdipShearMatrix(matrix, shearX, shearY, order);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipInvertMatrix(HandleRef matrix) => SkiaGdiPlus.GdipInvertMatrix(matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTransformMatrixPoints(HandleRef matrix, PointF* pts, int count) => SkiaGdiPlus.GdipTransformMatrixPoints(matrix, pts, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTransformMatrixPointsI(HandleRef matrix, Point* pts, int count) => SkiaGdiPlus.GdipTransformMatrixPointsI(matrix, pts, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipVectorTransformMatrixPoints(HandleRef matrix, PointF* pts, int count) => SkiaGdiPlus.GdipVectorTransformMatrixPoints(matrix, pts, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipVectorTransformMatrixPointsI(HandleRef matrix, Point* pts, int count) => SkiaGdiPlus.GdipVectorTransformMatrixPointsI(matrix, pts, count);

            [DllImport(LibraryName, ExactSpelling = true)]
            internal static extern unsafe int GdipGetMatrixElements(HandleRef matrix, float* m);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsMatrixInvertible(HandleRef matrix, out int boolean) => SkiaGdiPlus.GdipIsMatrixInvertible(matrix, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsMatrixIdentity(HandleRef matrix, out int boolean) => SkiaGdiPlus.GdipIsMatrixIdentity(matrix, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsMatrixEqual(HandleRef matrix, HandleRef matrix2, out int boolean) => SkiaGdiPlus.GdipIsMatrixEqual(matrix, matrix2, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegion(out IntPtr region) => SkiaGdiPlus.GdipCreateRegion(out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegionRect(ref RectangleF gprectf, out IntPtr region) => SkiaGdiPlus.GdipCreateRegionRect(ref gprectf, out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegionRectI(ref Rectangle gprect, out IntPtr region) => SkiaGdiPlus.GdipCreateRegionRectI(ref gprect, out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegionPath(HandleRef path, out IntPtr region) => SkiaGdiPlus.GdipCreateRegionPath(path, out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegionRgnData(byte[] rgndata, int size, out IntPtr region) => SkiaGdiPlus.GdipCreateRegionRgnData(rgndata, size, out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateRegionHrgn(IntPtr hRgn, out IntPtr region) => SkiaGdiPlus.GdipCreateRegionHrgn(hRgn, out region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneRegion(HandleRef region, out IntPtr cloneregion) => SkiaGdiPlus.GdipCloneRegion(region, out cloneregion);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteRegion(HandleRef region) => SkiaGdiPlus.GdipDeleteRegion(region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillRegion(HandleRef graphics, HandleRef brush, HandleRef region) => SkiaGdiPlus.GdipFillRegion(graphics, brush, region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetInfinite(HandleRef region) => SkiaGdiPlus.GdipSetInfinite(region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetEmpty(HandleRef region) => SkiaGdiPlus.GdipSetEmpty(region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCombineRegionRect(HandleRef region, ref RectangleF gprectf, CombineMode mode) => SkiaGdiPlus.GdipCombineRegionRect(region, ref gprectf, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCombineRegionRectI(HandleRef region, ref Rectangle gprect, CombineMode mode) => SkiaGdiPlus.GdipCombineRegionRectI(region, ref gprect, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCombineRegionPath(HandleRef region, HandleRef path, CombineMode mode) => SkiaGdiPlus.GdipCombineRegionPath(region, path, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCombineRegionRegion(HandleRef region, HandleRef region2, CombineMode mode) => SkiaGdiPlus.GdipCombineRegionRegion(region, region2, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateRegion(HandleRef region, float dx, float dy) => SkiaGdiPlus.GdipTranslateRegion(region, dx, dy);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateRegionI(HandleRef region, int dx, int dy) => SkiaGdiPlus.GdipTranslateRegionI(region, dx, dy);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTransformRegion(HandleRef region, HandleRef matrix) => SkiaGdiPlus.GdipTransformRegion(region, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionBounds(HandleRef region, HandleRef graphics, out RectangleF gprectf) => SkiaGdiPlus.GdipGetRegionBounds(region, graphics, out gprectf);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionHRgn(HandleRef region, HandleRef graphics, out IntPtr hrgn) => SkiaGdiPlus.GdipGetRegionHRgn(region, graphics, out hrgn);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsEmptyRegion(HandleRef region, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsEmptyRegion(region, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsInfiniteRegion(HandleRef region, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsInfiniteRegion(region, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsEqualRegion(HandleRef region, HandleRef region2, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsEqualRegion(region, region2, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionDataSize(HandleRef region, out int bufferSize) => SkiaGdiPlus.GdipGetRegionDataSize(region, out bufferSize);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionData(HandleRef region, byte[] regionData, int bufferSize, out int sizeFilled) => SkiaGdiPlus.GdipGetRegionData(region, regionData, bufferSize, out sizeFilled);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRegionPoint(HandleRef region, float X, float Y, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsVisibleRegionPoint(region, X, Y, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRegionPointI(HandleRef region, int X, int Y, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsVisibleRegionPointI(region, X, Y, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRegionRect(HandleRef region, float X, float Y, float width, float height, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsVisibleRegionRect(region, X, Y, width, height, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRegionRectI(HandleRef region, int X, int Y, int width, int height, HandleRef graphics, out int boolean) => SkiaGdiPlus.GdipIsVisibleRegionRectI(region, X, Y, width, height, graphics, out boolean);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionScansCount(HandleRef region, out int count, HandleRef matrix) => SkiaGdiPlus.GdipGetRegionScansCount(region, out count, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetRegionScans(HandleRef region, RectangleF* rects, out int count, HandleRef matrix) => SkiaGdiPlus.GdipGetRegionScans(region, rects, out count, matrix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics) => SkiaGdiPlus.GdipCreateFromHDC(hdc, out graphics);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetClipGraphics(HandleRef graphics, HandleRef srcgraphics, CombineMode mode) => SkiaGdiPlus.GdipSetClipGraphics(graphics, srcgraphics, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetClipRect(HandleRef graphics, float x, float y, float width, float height, CombineMode mode) => SkiaGdiPlus.GdipSetClipRect(graphics, x, y, width, height, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetClipRectI(HandleRef graphics, int x, int y, int width, int height, CombineMode mode) => SkiaGdiPlus.GdipSetClipRectI(graphics, x, y, width, height, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetClipPath(HandleRef graphics, HandleRef path, CombineMode mode) => SkiaGdiPlus.GdipSetClipPath(graphics, path, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetClipRegion(HandleRef graphics, HandleRef region, CombineMode mode) => SkiaGdiPlus.GdipSetClipRegion(graphics, region, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipResetClip(HandleRef graphics) => SkiaGdiPlus.GdipResetClip(graphics);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTranslateClip(HandleRef graphics, float dx, float dy) => SkiaGdiPlus.GdipTranslateClip(graphics, dx, dy);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetClip(HandleRef graphics, HandleRef region) => SkiaGdiPlus.GdipGetClip(graphics, region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetClipBounds(HandleRef graphics, out RectangleF rect) => SkiaGdiPlus.GdipGetClipBounds(graphics, out rect);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsClipEmpty(HandleRef graphics, out bool result) => SkiaGdiPlus.GdipIsClipEmpty(graphics, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetVisibleClipBounds(HandleRef graphics, out RectangleF rect) => SkiaGdiPlus.GdipGetVisibleClipBounds(graphics, out rect);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleClipEmpty(HandleRef graphics, out bool result) => SkiaGdiPlus.GdipIsVisibleClipEmpty(graphics, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisiblePoint(HandleRef graphics, float x, float y, out bool result) => SkiaGdiPlus.GdipIsVisiblePoint(graphics, x, y, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisiblePointI(HandleRef graphics, int x, int y, out bool result) => SkiaGdiPlus.GdipIsVisiblePointI(graphics, x, y, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRect(HandleRef graphics, float x, float y, float width, float height, out bool result) => SkiaGdiPlus.GdipIsVisibleRect(graphics, x, y, width, height, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipIsVisibleRectI(HandleRef graphics, int x, int y, int width, int height, out bool result) => SkiaGdiPlus.GdipIsVisibleRectI(graphics, x, y, width, height, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFlush(HandleRef graphics, FlushIntention intention) => SkiaGdiPlus.GdipFlush(graphics, intention);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetDC(HandleRef graphics, out IntPtr hdc) => SkiaGdiPlus.GdipGetDC(graphics, out hdc);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatMeasurableCharacterRanges(HandleRef format, int rangeCount, [In] [Out] CharacterRange[] range) => SkiaGdiPlus.GdipSetStringFormatMeasurableCharacterRanges(format, rangeCount, range);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateStringFormat(StringFormatFlags options, int language, out IntPtr format) => SkiaGdiPlus.GdipCreateStringFormat(options, language, out format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipStringFormatGetGenericDefault(out IntPtr format) => SkiaGdiPlus.GdipStringFormatGetGenericDefault(out format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipStringFormatGetGenericTypographic(out IntPtr format) => SkiaGdiPlus.GdipStringFormatGetGenericTypographic(out format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDeleteStringFormat(HandleRef format) => SkiaGdiPlus.GdipDeleteStringFormat(format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneStringFormat(HandleRef format, out IntPtr newFormat) => SkiaGdiPlus.GdipCloneStringFormat(format, out newFormat);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatFlags(HandleRef format, StringFormatFlags options) => SkiaGdiPlus.GdipSetStringFormatFlags(format, options);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatFlags(HandleRef format, out StringFormatFlags result) => SkiaGdiPlus.GdipGetStringFormatFlags(format, out result);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatAlign(HandleRef format, StringAlignment align) => SkiaGdiPlus.GdipSetStringFormatAlign(format, align);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatAlign(HandleRef format, out StringAlignment align) => SkiaGdiPlus.GdipGetStringFormatAlign(format, out align);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatLineAlign(HandleRef format, StringAlignment align) => SkiaGdiPlus.GdipSetStringFormatLineAlign(format, align);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatLineAlign(HandleRef format, out StringAlignment align) => SkiaGdiPlus.GdipGetStringFormatLineAlign(format, out align);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatHotkeyPrefix(HandleRef format, HotkeyPrefix hotkeyPrefix) => SkiaGdiPlus.GdipSetStringFormatHotkeyPrefix(format, hotkeyPrefix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatHotkeyPrefix(HandleRef format, out HotkeyPrefix hotkeyPrefix) => SkiaGdiPlus.GdipGetStringFormatHotkeyPrefix(format, out hotkeyPrefix);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatTabStops(HandleRef format, float firstTabOffset, int count, float[] tabStops) => SkiaGdiPlus.GdipSetStringFormatTabStops(format, firstTabOffset, count, tabStops);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatTabStops(HandleRef format, int count, out float firstTabOffset, [In] [Out] float[] tabStops) => SkiaGdiPlus.GdipGetStringFormatTabStops(format, count, out firstTabOffset, tabStops);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatTabStopCount(HandleRef format, out int count) => SkiaGdiPlus.GdipGetStringFormatTabStopCount(format, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatMeasurableCharacterRangeCount(HandleRef format, out int count) => SkiaGdiPlus.GdipGetStringFormatMeasurableCharacterRangeCount(format, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatTrimming(HandleRef format, StringTrimming trimming) => SkiaGdiPlus.GdipSetStringFormatTrimming(format, trimming);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatTrimming(HandleRef format, out StringTrimming trimming) => SkiaGdiPlus.GdipGetStringFormatTrimming(format, out trimming);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetStringFormatDigitSubstitution(HandleRef format, int langID, StringDigitSubstitute sds) => SkiaGdiPlus.GdipSetStringFormatDigitSubstitution(format, langID, sds);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetStringFormatDigitSubstitution(HandleRef format, out int langID, out StringDigitSubstitute sds) => SkiaGdiPlus.GdipGetStringFormatDigitSubstitution(format, out langID, out sds);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageDimension(HandleRef image, out float width, out float height) => SkiaGdiPlus.GdipGetImageDimension(image, out width, out height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageWidth(HandleRef image, out int width) => SkiaGdiPlus.GdipGetImageWidth(image, out width);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageHeight(HandleRef image, out int height) => SkiaGdiPlus.GdipGetImageHeight(image, out height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageHorizontalResolution(HandleRef image, out float horzRes) => SkiaGdiPlus.GdipGetImageHorizontalResolution(image, out horzRes);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageVerticalResolution(HandleRef image, out float vertRes) => SkiaGdiPlus.GdipGetImageVerticalResolution(image, out vertRes);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageFlags(HandleRef image, out int flags) => SkiaGdiPlus.GdipGetImageFlags(image, out flags);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageRawFormat(HandleRef image, ref Guid format) => SkiaGdiPlus.GdipGetImageRawFormat(image, ref format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImagePixelFormat(HandleRef image, out PixelFormat format) => SkiaGdiPlus.GdipGetImagePixelFormat(image, out format);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipImageGetFrameCount(HandleRef image, ref Guid dimensionID, out int count) => SkiaGdiPlus.GdipImageGetFrameCount(image, ref dimensionID, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipImageSelectActiveFrame(HandleRef image, ref Guid dimensionID, int frameIndex) => SkiaGdiPlus.GdipImageSelectActiveFrame(image, ref dimensionID, frameIndex);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipImageRotateFlip(HandleRef image, int rotateFlipType) => SkiaGdiPlus.GdipImageRotateFlip(image, rotateFlipType);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetAllPropertyItems(HandleRef image, uint totalBufferSize, uint numProperties, PropertyItemInternal* allItems) => SkiaGdiPlus.GdipGetAllPropertyItems(image, totalBufferSize, numProperties, allItems);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPropertyCount(HandleRef image, out uint numOfProperty) => SkiaGdiPlus.GdipGetPropertyCount(image, out numOfProperty);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPropertyIdList(HandleRef image, uint numOfProperty, int* list) => SkiaGdiPlus.GdipGetPropertyIdList(image, numOfProperty, list);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPropertyItem(HandleRef image, int propid, uint propSize, PropertyItemInternal* buffer) => SkiaGdiPlus.GdipGetPropertyItem(image, propid, propSize, buffer);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPropertyItemSize(HandleRef image, int propid, out uint size) => SkiaGdiPlus.GdipGetPropertyItemSize(image, propid, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetPropertySize(HandleRef image, out uint totalBufferSize, out uint numProperties) => SkiaGdiPlus.GdipGetPropertySize(image, out totalBufferSize, out numProperties);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRemovePropertyItem(HandleRef image, int propid) => SkiaGdiPlus.GdipRemovePropertyItem(image, propid);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSetPropertyItem(HandleRef image, PropertyItemInternal* item) => SkiaGdiPlus.GdipSetPropertyItem(image, item);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageType(HandleRef image, out int type) => SkiaGdiPlus.GdipGetImageType(image, out type);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetImageType(IntPtr image, out int type) => SkiaGdiPlus.GdipGetImageType(image, out type);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDisposeImage(HandleRef image) => SkiaGdiPlus.GdipDisposeImage(image);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDisposeImage(IntPtr image) => SkiaGdiPlus.GdipDisposeImage(image);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromFile(filename, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromFileICM(string filename, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromFileICM(filename, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromScan0(width, height, stride, format, scan0, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromGraphics(int width, int height, HandleRef graphics, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromGraphics(width, height, graphics, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromHBITMAP(IntPtr hbitmap, IntPtr hpalette, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromHBITMAP(hbitmap, hpalette, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromHICON(IntPtr hicon, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromHICON(hicon, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateBitmapFromResource(IntPtr hresource, IntPtr name, out IntPtr bitmap) => SkiaGdiPlus.GdipCreateBitmapFromResource(hresource, name, out bitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateHBITMAPFromBitmap(HandleRef nativeBitmap, out IntPtr hbitmap, int argbBackground) => SkiaGdiPlus.GdipCreateHBITMAPFromBitmap(nativeBitmap, out hbitmap, argbBackground);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateHICONFromBitmap(HandleRef nativeBitmap, out IntPtr hicon) => SkiaGdiPlus.GdipCreateHICONFromBitmap(nativeBitmap, out hicon);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneBitmapArea(float x, float y, float width, float height, int format, HandleRef srcbitmap, out IntPtr dstbitmap) => SkiaGdiPlus.GdipCloneBitmapArea(x, y, width, height, format, srcbitmap, out dstbitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCloneBitmapAreaI(int x, int y, int width, int height, int format, HandleRef srcbitmap, out IntPtr dstbitmap) => SkiaGdiPlus.GdipCloneBitmapAreaI(x, y, width, height, format, srcbitmap, out dstbitmap);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBitmapLockBits(HandleRef bitmap, ref Rectangle rect, ImageLockMode flags, PixelFormat format, [In] [Out] BitmapData lockedBitmapData) => SkiaGdiPlus.GdipBitmapLockBits(bitmap, ref rect, flags, format, lockedBitmapData);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBitmapUnlockBits(HandleRef bitmap, BitmapData lockedBitmapData) => SkiaGdiPlus.GdipBitmapUnlockBits(bitmap, lockedBitmapData);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBitmapGetPixel(HandleRef bitmap, int x, int y, out int argb) => SkiaGdiPlus.GdipBitmapGetPixel(bitmap, x, y, out argb);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBitmapSetPixel(HandleRef bitmap, int x, int y, int argb) => SkiaGdiPlus.GdipBitmapSetPixel(bitmap, x, y, argb);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipBitmapSetResolution(HandleRef bitmap, float dpix, float dpiy) => SkiaGdiPlus.GdipBitmapSetResolution(bitmap, dpix, dpiy);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipImageGetFrameDimensionsCount(HandleRef image, out int count) => SkiaGdiPlus.GdipImageGetFrameDimensionsCount(image, out count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipImageGetFrameDimensionsList(HandleRef image, Guid* dimensionIDs, int count) => SkiaGdiPlus.GdipImageGetFrameDimensionsList(image, dimensionIDs, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMetafileFromEmf(IntPtr hEnhMetafile, bool deleteEmf, out IntPtr metafile) => SkiaGdiPlus.GdipCreateMetafileFromEmf(hEnhMetafile, deleteEmf, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMetafileFromWmf(IntPtr hMetafile, bool deleteWmf, WmfPlaceableFileHeader wmfplacealbeHeader, out IntPtr metafile) => SkiaGdiPlus.GdipCreateMetafileFromWmf(hMetafile, deleteWmf, wmfplacealbeHeader, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipCreateMetafileFromFile(string file, out IntPtr metafile) => SkiaGdiPlus.GdipCreateMetafileFromFile(file, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafile(IntPtr referenceHdc, EmfType emfType, IntPtr pframeRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafile(referenceHdc, emfType, pframeRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafile(IntPtr referenceHdc, EmfType emfType, ref RectangleF frameRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafile(referenceHdc, emfType, ref frameRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafileI(IntPtr referenceHdc, EmfType emfType, ref Rectangle frameRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafileI(referenceHdc, emfType, ref frameRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafileFileName(string fileName, IntPtr referenceHdc, EmfType emfType, ref RectangleF frameRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafileFileName(fileName, referenceHdc, emfType, ref frameRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafileFileName(string fileName, IntPtr referenceHdc, EmfType emfType, IntPtr pframeRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafileFileName(fileName, referenceHdc, emfType, pframeRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipRecordMetafileFileNameI(string fileName, IntPtr referenceHdc, EmfType emfType, ref Rectangle frameRect, MetafileFrameUnit frameUnit, string? description, out IntPtr metafile) => SkiaGdiPlus.GdipRecordMetafileFileNameI(fileName, referenceHdc, emfType, ref frameRect, frameUnit, description, out metafile);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipPlayMetafileRecord(HandleRef metafile, EmfPlusRecordType recordType, int flags, int dataSize, byte[] data) => SkiaGdiPlus.GdipPlayMetafileRecord(metafile, recordType, flags, dataSize, data);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipSaveGraphics(HandleRef graphics, out int state) => SkiaGdiPlus.GdipSaveGraphics(graphics, out state);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawArc(HandleRef graphics, HandleRef pen, float x, float y, float width, float height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipDrawArc(graphics, pen, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawArcI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipDrawArcI(graphics, pen, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawLinesI(HandleRef graphics, HandleRef pen, Point* points, int count) => SkiaGdiPlus.GdipDrawLinesI(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawBezier(HandleRef graphics, HandleRef pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) => SkiaGdiPlus.GdipDrawBezier(graphics, pen, x1, y1, x2, y2, x3, y3, x4, y4);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawEllipse(HandleRef graphics, HandleRef pen, float x, float y, float width, float height) => SkiaGdiPlus.GdipDrawEllipse(graphics, pen, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawEllipseI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height) => SkiaGdiPlus.GdipDrawEllipseI(graphics, pen, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawLine(HandleRef graphics, HandleRef pen, float x1, float y1, float x2, float y2) => SkiaGdiPlus.GdipDrawLine(graphics, pen, x1, y1, x2, y2);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawLineI(HandleRef graphics, HandleRef pen, int x1, int y1, int x2, int y2) => SkiaGdiPlus.GdipDrawLineI(graphics, pen, x1, y1, x2, y2);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawLines(HandleRef graphics, HandleRef pen, PointF* points, int count) => SkiaGdiPlus.GdipDrawLines(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawPath(HandleRef graphics, HandleRef pen, HandleRef path) => SkiaGdiPlus.GdipDrawPath(graphics, pen, path);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawPie(HandleRef graphics, HandleRef pen, float x, float y, float width, float height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipDrawPie(graphics, pen, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawPieI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipDrawPieI(graphics, pen, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawPolygon(HandleRef graphics, HandleRef pen, PointF* points, int count) => SkiaGdiPlus.GdipDrawPolygon(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawPolygonI(HandleRef graphics, HandleRef pen, Point* points, int count) => SkiaGdiPlus.GdipDrawPolygonI(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillEllipse(HandleRef graphics, HandleRef brush, float x, float y, float width, float height) => SkiaGdiPlus.GdipFillEllipse(graphics, brush, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillEllipseI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height) => SkiaGdiPlus.GdipFillEllipseI(graphics, brush, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillPolygon(HandleRef graphics, HandleRef brush, PointF* points, int count, FillMode brushMode) => SkiaGdiPlus.GdipFillPolygon(graphics, brush, points, count, brushMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillPolygonI(HandleRef graphics, HandleRef brush, Point* points, int count, FillMode brushMode) => SkiaGdiPlus.GdipFillPolygonI(graphics, brush, points, count, brushMode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillRectangle(HandleRef graphics, HandleRef brush, float x, float y, float width, float height) => SkiaGdiPlus.GdipFillRectangle(graphics, brush, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillRectangleI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height) => SkiaGdiPlus.GdipFillRectangleI(graphics, brush, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillRectangles(HandleRef graphics, HandleRef brush, RectangleF* rects, int count) => SkiaGdiPlus.GdipFillRectangles(graphics, brush, rects, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillRectanglesI(HandleRef graphics, HandleRef brush, Rectangle* rects, int count) => SkiaGdiPlus.GdipFillRectanglesI(graphics, brush, rects, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawString(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, HandleRef brush) => SkiaGdiPlus.GdipDrawString(graphics, textString, length, font, ref layoutRect, stringFormat, brush);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImageRectI(HandleRef graphics, HandleRef image, int x, int y, int width, int height) => SkiaGdiPlus.GdipDrawImageRectI(graphics, image, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGraphicsClear(HandleRef graphics, int argb) => SkiaGdiPlus.GdipGraphicsClear(graphics, argb);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawClosedCurve(HandleRef graphics, HandleRef pen, PointF* points, int count) => SkiaGdiPlus.GdipDrawClosedCurve(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawClosedCurveI(HandleRef graphics, HandleRef pen, Point* points, int count) => SkiaGdiPlus.GdipDrawClosedCurveI(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawClosedCurve2(HandleRef graphics, HandleRef pen, PointF* points, int count, float tension) => SkiaGdiPlus.GdipDrawClosedCurve2(graphics, pen, points, count, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawClosedCurve2I(HandleRef graphics, HandleRef pen, Point* points, int count, float tension) => SkiaGdiPlus.GdipDrawClosedCurve2I(graphics, pen, points, count, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurve(HandleRef graphics, HandleRef pen, PointF* points, int count) => SkiaGdiPlus.GdipDrawCurve(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurveI(HandleRef graphics, HandleRef pen, Point* points, int count) => SkiaGdiPlus.GdipDrawCurveI(graphics, pen, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurve2(HandleRef graphics, HandleRef pen, PointF* points, int count, float tension) => SkiaGdiPlus.GdipDrawCurve2(graphics, pen, points, count, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurve2I(HandleRef graphics, HandleRef pen, Point* points, int count, float tension) => SkiaGdiPlus.GdipDrawCurve2I(graphics, pen, points, count, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurve3(HandleRef graphics, HandleRef pen, PointF* points, int count, int offset, int numberOfSegments, float tension) => SkiaGdiPlus.GdipDrawCurve3(graphics, pen, points, count, offset, numberOfSegments, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawCurve3I(HandleRef graphics, HandleRef pen, Point* points, int count, int offset, int numberOfSegments, float tension) => SkiaGdiPlus.GdipDrawCurve3I(graphics, pen, points, count, offset, numberOfSegments, tension);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillClosedCurve(HandleRef graphics, HandleRef brush, PointF* points, int count) => SkiaGdiPlus.GdipFillClosedCurve(graphics, brush, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillClosedCurveI(HandleRef graphics, HandleRef brush, Point* points, int count) => SkiaGdiPlus.GdipFillClosedCurveI(graphics, brush, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillClosedCurve2(HandleRef graphics, HandleRef brush, PointF* points, int count, float tension, FillMode mode) => SkiaGdiPlus.GdipFillClosedCurve2(graphics, brush, points, count, tension, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillClosedCurve2I(HandleRef graphics, HandleRef brush, Point* points, int count, float tension, FillMode mode) => SkiaGdiPlus.GdipFillClosedCurve2I(graphics, brush, points, count, tension, mode);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillPie(HandleRef graphics, HandleRef brush, float x, float y, float width, float height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipFillPie(graphics, brush, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipFillPieI(HandleRef graphics, HandleRef brush, int x, int y, int width, int height, float startAngle, float sweepAngle) => SkiaGdiPlus.GdipFillPieI(graphics, brush, x, y, width, height, startAngle, sweepAngle);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMeasureString(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, ref RectangleF boundingBox, out int codepointsFitted, out int linesFilled) => SkiaGdiPlus.GdipMeasureString(graphics, textString, length, font, ref layoutRect, stringFormat, ref boundingBox, out codepointsFitted, out linesFilled);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipMeasureCharacterRanges(HandleRef graphics, string textString, int length, HandleRef font, ref RectangleF layoutRect, HandleRef stringFormat, int characterCount, [In] [Out] IntPtr[] region) => SkiaGdiPlus.GdipMeasureCharacterRanges(graphics, textString, length, font, ref layoutRect, stringFormat, characterCount, region);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImageI(HandleRef graphics, HandleRef image, int x, int y) => SkiaGdiPlus.GdipDrawImageI(graphics, image, x, y);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImage(HandleRef graphics, HandleRef image, float x, float y) => SkiaGdiPlus.GdipDrawImage(graphics, image, x, y);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePoints(HandleRef graphics, HandleRef image, PointF* points, int count) => SkiaGdiPlus.GdipDrawImagePoints(graphics, image, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePointsI(HandleRef graphics, HandleRef image, Point* points, int count) => SkiaGdiPlus.GdipDrawImagePointsI(graphics, image, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImageRectRectI(HandleRef graphics, HandleRef image, int dstx, int dsty, int dstwidth, int dstheight, int srcx, int srcy, int srcwidth, int srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata) => SkiaGdiPlus.GdipDrawImageRectRectI(graphics, image, dstx, dsty, dstwidth, dstheight, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePointsRect(HandleRef graphics, HandleRef image, PointF* points, int count, float srcx, float srcy, float srcwidth, float srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata) => SkiaGdiPlus.GdipDrawImagePointsRect(graphics, image, points, count, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImageRectRect(HandleRef graphics, HandleRef image, float dstx, float dsty, float dstwidth, float dstheight, float srcx, float srcy, float srcwidth, float srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata) => SkiaGdiPlus.GdipDrawImageRectRect(graphics, image, dstx, dsty, dstwidth, dstheight, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePointsRectI(HandleRef graphics, HandleRef image, Point* points, int count, int srcx, int srcy, int srcwidth, int srcheight, GraphicsUnit srcunit, HandleRef imageAttributes, Graphics.DrawImageAbort? callback, HandleRef callbackdata) => SkiaGdiPlus.GdipDrawImagePointsRectI(graphics, image, points, count, srcx, srcy, srcwidth, srcheight, srcunit, imageAttributes, callback, callbackdata);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImageRect(HandleRef graphics, HandleRef image, float x, float y, float width, float height) => SkiaGdiPlus.GdipDrawImageRect(graphics, image, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePointRect(HandleRef graphics, HandleRef image, float x, float y, float srcx, float srcy, float srcwidth, float srcheight, int srcunit) => SkiaGdiPlus.GdipDrawImagePointRect(graphics, image, x, y, srcx, srcy, srcwidth, srcheight, srcunit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawImagePointRectI(HandleRef graphics, HandleRef image, int x, int y, int srcx, int srcy, int srcwidth, int srcheight, int srcunit) => SkiaGdiPlus.GdipDrawImagePointRectI(graphics, image, x, y, srcx, srcy, srcwidth, srcheight, srcunit);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawRectangle(HandleRef graphics, HandleRef pen, float x, float y, float width, float height) => SkiaGdiPlus.GdipDrawRectangle(graphics, pen, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawRectangleI(HandleRef graphics, HandleRef pen, int x, int y, int width, int height) => SkiaGdiPlus.GdipDrawRectangleI(graphics, pen, x, y, width, height);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawRectangles(HandleRef graphics, HandleRef pen, RectangleF* rects, int count) => SkiaGdiPlus.GdipDrawRectangles(graphics, pen, rects, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipDrawRectanglesI(HandleRef graphics, HandleRef pen, Rectangle* rects, int count) => SkiaGdiPlus.GdipDrawRectanglesI(graphics, pen, rects, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTransformPoints(HandleRef graphics, int destSpace, int srcSpace, PointF* points, int count) => SkiaGdiPlus.GdipTransformPoints(graphics, destSpace, srcSpace, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipTransformPointsI(HandleRef graphics, int destSpace, int srcSpace, Point* points, int count) => SkiaGdiPlus.GdipTransformPointsI(graphics, destSpace, srcSpace, points, count);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipLoadImageFromFileICM(string filename, out IntPtr image) => SkiaGdiPlus.GdipLoadImageFromFileICM(filename, out image);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipLoadImageFromFile(string filename, out IntPtr image) => SkiaGdiPlus.GdipLoadImageFromFile(filename, out image);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetEncoderParameterListSize(HandleRef image, ref Guid encoder, out int size) => SkiaGdiPlus.GdipGetEncoderParameterListSize(image, ref encoder, out size);

            // webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).
            internal static int GdipGetEncoderParameterList(HandleRef image, ref Guid encoder, int size, IntPtr buffer) => SkiaGdiPlus.GdipGetEncoderParameterList(image, ref encoder, size, buffer);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupInput
        {
            public int GdiplusVersion;             // Must be 1

            // public DebugEventProc DebugEventCallback; // Ignored on free builds
            public IntPtr DebugEventCallback;

            public bool SuppressBackgroundThread;     // FALSE unless you're prepared to call
                                                      // the hook/unhook functions properly

            public bool SuppressExternalCodecs;       // FALSE unless you want GDI+ only to use
                                                      // its internal image codecs.

            public static StartupInput GetDefault()
            {
                OperatingSystem os = Environment.OSVersion;
                StartupInput result = default;

                // In Windows 7 GDI+1.1 story is different as there are different binaries per GDI+ version.
                bool isWindows7 = os.Platform == PlatformID.Win32NT && os.Version.Major == 6 && os.Version.Minor == 1;
                result.GdiplusVersion = isWindows7 ? 1 : 2;
                // result.DebugEventCallback = null;
                result.SuppressBackgroundThread = false;
                result.SuppressExternalCodecs = false;
                return result;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupOutput
        {
            // The following 2 fields won't be used.  They were originally intended
            // for getting GDI+ to run on our thread - however there are marshalling
            // dealing with function *'s and what not - so we make explicit calls
            // to gdi+ after the fact, via the GdiplusNotificationHook and
            // GdiplusNotificationUnhook methods.
            public IntPtr hook; //not used
            public IntPtr unhook; //not used.
        }
    }
}
