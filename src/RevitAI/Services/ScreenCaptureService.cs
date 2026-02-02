// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// Provides screenshot capture capabilities for Revit views and windows.
/// Supports both full window capture (via Windows API) and view-only export (via Revit API).
/// </summary>
public sealed class ScreenCaptureService
{
    #region Windows API Imports

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    #endregion

    /// <summary>
    /// Resolution presets mapped to pixel widths.
    /// </summary>
    private static readonly Dictionary<ScreenshotResolution, int> ResolutionWidths = new()
    {
        { ScreenshotResolution.Low, 800 },
        { ScreenshotResolution.Medium, 1280 },
        { ScreenshotResolution.High, 1920 },
        { ScreenshotResolution.Max, 2560 }
    };

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static ScreenCaptureService Instance { get; } = new();

    private ScreenCaptureService() { }

    /// <summary>
    /// Captures the entire Revit window including UI elements.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    /// <param name="resolution">The target resolution for the screenshot.</param>
    /// <returns>A capture result containing the image bytes or error.</returns>
    public CaptureResult CaptureRevitWindow(UIApplication app, ScreenshotResolution resolution)
    {
        try
        {
            var mainWindowHandle = app.MainWindowHandle;

            if (mainWindowHandle == IntPtr.Zero)
            {
                return CaptureResult.Failed("Could not get Revit main window handle.");
            }

            if (IsIconic(mainWindowHandle))
            {
                return CaptureResult.Failed("Cannot capture screenshot while Revit is minimized.");
            }

            if (!GetWindowRect(mainWindowHandle, out var rect))
            {
                return CaptureResult.Failed("Could not get Revit window dimensions.");
            }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return CaptureResult.Failed("Invalid window dimensions.");
            }

            // Capture the window at its current size
            using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height));
            }

            // Resize to target resolution
            var targetWidth = ResolutionWidths[resolution];
            using var resizedBitmap = ResizeImage(bitmap, targetWidth);

            // Convert to PNG bytes
            using var ms = new MemoryStream();
            resizedBitmap.Save(ms, ImageFormat.Png);

            return CaptureResult.Success(ms.ToArray(), "image/png", CaptureMode.FullWindow);
        }
        catch (Exception ex)
        {
            return CaptureResult.Failed($"Window capture failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures only the active view using Revit's ExportImage API.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    /// <param name="resolution">The target resolution for the screenshot.</param>
    /// <returns>A capture result containing the image bytes or error.</returns>
    public CaptureResult CaptureActiveView(UIApplication app, ScreenshotResolution resolution)
    {
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            var view = app.ActiveUIDocument?.ActiveView;

            if (doc == null)
            {
                return CaptureResult.Failed("No active document. Please open a Revit project first.");
            }

            if (view == null)
            {
                return CaptureResult.Failed("No active view.");
            }

            if (!CanExportView(view))
            {
                // Return error - caller should handle fallback
                return CaptureResult.Failed($"View type '{view.ViewType}' cannot be exported. Use full window capture instead.");
            }

            var targetWidth = ResolutionWidths[resolution];
            var tempPath = Path.Combine(Path.GetTempPath(), $"RevitAI_view_{Guid.NewGuid()}.png");

            try
            {
                var options = new ImageExportOptions
                {
                    FilePath = tempPath,
                    ExportRange = ExportRange.CurrentView,
                    ZoomType = ZoomFitType.FitToPage,
                    ImageResolution = GetImageResolution(resolution),
                    PixelSize = targetWidth,
                    HLRandWFViewsFileType = ImageFileType.PNG
                };

                doc.ExportImage(options);

                // Find the actual exported file (Revit may add suffix)
                var actualPath = FindExportedFile(tempPath);
                if (actualPath == null)
                {
                    return CaptureResult.Failed("Export completed but file not found.");
                }

                var bytes = File.ReadAllBytes(actualPath);
                return CaptureResult.Success(bytes, "image/png", CaptureMode.ViewOnly);
            }
            finally
            {
                // Clean up temp files
                CleanupTempFiles(tempPath);
            }
        }
        catch (Exception ex)
        {
            return CaptureResult.Failed($"View export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if a view type can be exported using Revit's ExportImage API.
    /// </summary>
    /// <param name="view">The view to check.</param>
    /// <returns>True if the view can be exported, false otherwise.</returns>
    public bool CanExportView(View view)
    {
        // These view types cannot be exported as images
        return view.ViewType switch
        {
            ViewType.Schedule => false,
            ViewType.DrawingSheet => false,
            ViewType.ProjectBrowser => false,
            ViewType.SystemBrowser => false,
            ViewType.Undefined => false,
            ViewType.Internal => false,
            ViewType.Report => false,
            ViewType.CostReport => false,
            ViewType.LoadsReport => false,
            ViewType.PresureLossReport => false,
            ViewType.PanelSchedule => false,
            ViewType.ColumnSchedule => false,
            _ => true
        };
    }

    /// <summary>
    /// Resizes an image while maintaining aspect ratio using high-quality bicubic interpolation.
    /// </summary>
    /// <param name="source">The source bitmap to resize.</param>
    /// <param name="targetWidth">The target width in pixels.</param>
    /// <returns>A new resized bitmap.</returns>
    private static Bitmap ResizeImage(Bitmap source, int targetWidth)
    {
        // Don't upscale
        if (source.Width <= targetWidth)
        {
            return new Bitmap(source);
        }

        var aspectRatio = (double)source.Height / source.Width;
        var targetHeight = (int)(targetWidth * aspectRatio);

        var destRect = new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight);
        var destImage = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);

        destImage.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using (var graphics = Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var wrapMode = new ImageAttributes();
            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(source, destRect, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);
        }

        return destImage;
    }

    /// <summary>
    /// Maps resolution enum to Revit ImageResolution.
    /// </summary>
    private static ImageResolution GetImageResolution(ScreenshotResolution resolution)
    {
        return resolution switch
        {
            ScreenshotResolution.Low => ImageResolution.DPI_72,
            ScreenshotResolution.Medium => ImageResolution.DPI_150,
            ScreenshotResolution.High => ImageResolution.DPI_300,
            ScreenshotResolution.Max => ImageResolution.DPI_600,
            _ => ImageResolution.DPI_150
        };
    }

    /// <summary>
    /// Finds the actual exported file path (Revit may add suffixes).
    /// </summary>
    private static string? FindExportedFile(string basePath)
    {
        if (File.Exists(basePath))
        {
            return basePath;
        }

        var directory = Path.GetDirectoryName(basePath);
        var baseName = Path.GetFileNameWithoutExtension(basePath);

        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var possibleFiles = Directory.GetFiles(directory, $"{baseName}*.png");
        return possibleFiles.Length > 0 ? possibleFiles[0] : null;
    }

    /// <summary>
    /// Cleans up temporary export files.
    /// </summary>
    private static void CleanupTempFiles(string basePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(basePath);
            var baseName = Path.GetFileNameWithoutExtension(basePath);

            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(directory, $"{baseName}*.png"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore individual file deletion errors
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// Indicates how a screenshot was captured.
/// </summary>
public enum CaptureMode
{
    /// <summary>Full window capture including UI.</summary>
    FullWindow,

    /// <summary>View-only export without UI.</summary>
    ViewOnly
}

/// <summary>
/// Represents the result of a screenshot capture operation.
/// </summary>
public sealed class CaptureResult
{
    /// <summary>
    /// Gets whether the capture was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the captured image bytes (null if failed).
    /// </summary>
    public byte[]? ImageBytes { get; }

    /// <summary>
    /// Gets the media type of the image (e.g., "image/png").
    /// </summary>
    public string? MediaType { get; }

    /// <summary>
    /// Gets the capture mode used.
    /// </summary>
    public CaptureMode? Mode { get; }

    /// <summary>
    /// Gets the error message (null if successful).
    /// </summary>
    public string? ErrorMessage { get; }

    private CaptureResult(bool isSuccess, byte[]? imageBytes, string? mediaType, CaptureMode? mode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ImageBytes = imageBytes;
        MediaType = mediaType;
        Mode = mode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful capture result.
    /// </summary>
    public static CaptureResult Success(byte[] imageBytes, string mediaType, CaptureMode mode)
    {
        return new CaptureResult(true, imageBytes, mediaType, mode, null);
    }

    /// <summary>
    /// Creates a failed capture result.
    /// </summary>
    public static CaptureResult Failed(string errorMessage)
    {
        return new CaptureResult(false, null, null, null, errorMessage);
    }
}
