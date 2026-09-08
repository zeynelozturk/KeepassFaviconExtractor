using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace FaviconExtractor
{
    internal static class IconNormalizer
    {
        private const int SvgAnalysisCanvasSize = 512;
        private const int SvgRawRenderCanvasSize = 1024;
        private const byte OpaqueAlphaThreshold = 8;
        private const double MinOpaquePixelRatio = 0.0005d;
        private const double MinContentAreaRatio = 0.01d;
        private const double MaxContentAspectRatio = 10.0d;
        private const double OppositeMarginSuspicionRatio = 0.20d;
        private const double MaxCenterOffsetSuspicionRatio = 0.35d;
        private const double ExcessMarginRatioForTrim = 0.55d;

        public static byte[] NormalizeToPng(byte[] sourceBytes, string contentType, string sourceUrl)
        {
            return NormalizeToPng(sourceBytes, contentType, sourceUrl, CancellationToken.None);
        }

        public static byte[] NormalizeToPng(byte[] sourceBytes, string contentType, string sourceUrl, CancellationToken cancellationToken)
        {
            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                throw new ArgumentException("Source icon bytes are empty.", nameof(sourceBytes));
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool isSvgSource = IsSvg(contentType, sourceUrl, sourceBytes);

            using (Bitmap sourceBitmap = DecodeToBitmap(sourceBytes, contentType, sourceUrl, isSvgSource, cancellationToken))
            using (Bitmap normalizedBitmap = NormalizeBitmap(sourceBitmap, isSvgSource))
            using (MemoryStream outputStream = new MemoryStream())
            {
                cancellationToken.ThrowIfCancellationRequested();
                normalizedBitmap.Save(outputStream, ImageFormat.Png);
                return outputStream.ToArray();
            }
        }

        private static Bitmap DecodeToBitmap(byte[] sourceBytes, string contentType, string sourceUrl, bool isSvgSource, CancellationToken cancellationToken)
        {
            if (isSvgSource)
            {
                return DecodeSvgToBitmap(sourceBytes, sourceUrl, cancellationToken);
            }

            if (IsIco(contentType, sourceUrl, sourceBytes))
            {
                try
                {
                    return DecodeIcoToBitmap(sourceBytes);
                }
                catch (ArgumentException)
                {
                    // Some endpoints expose PNG/JPEG bytes from a .ico URL.
                    // Fall back to regular image decoding when ICO parsing fails.
                }
            }

            using (MemoryStream stream = new MemoryStream(sourceBytes))
            using (Image image = Image.FromStream(stream, true, true))
            {
                return new Bitmap(image);
            }
        }

        private static Bitmap DecodeSvgToBitmap(byte[] svgBytes, string sourceUrl, CancellationToken cancellationToken)
        {
            using (Bitmap renderedBitmap = RunInStaThread(delegate
            {
                WpfDrawingSettings settings = new WpfDrawingSettings();
                settings.IncludeRuntime = false;

                using (MemoryStream input = new MemoryStream(svgBytes))
                using (MemoryStream output = new MemoryStream())
                {
                    StreamSvgConverter converter = new StreamSvgConverter(settings);
                    bool converted = converter.Convert(input, output);
                    if (!converted)
                    {
                        throw new InvalidOperationException("SVG conversion failed.");
                    }

                    output.Position = 0;
                    using (Image image = Image.FromStream(output, true, true))
                    {
                        return new Bitmap(image);
                    }
                }
            }, cancellationToken, FaviconDiscoveryPreferences.SvgStaOperationTimeout))
            using (Bitmap rawRenderCanvas = CreateFittedCanvas(renderedBitmap, SvgRawRenderCanvasSize, SvgRawRenderCanvasSize))
            {
                return ValidateAndPrepareSvgBitmap(rawRenderCanvas, sourceUrl, cancellationToken);
            }
        }

        private static Bitmap ValidateAndPrepareSvgBitmap(Bitmap svgBitmap, string sourceUrl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OpaqueMetrics sourceMetrics = MeasureOpaqueMetrics(svgBitmap);
            string suspicionReason = GetSuspiciousSvgReason(sourceMetrics, svgBitmap.Width, svgBitmap.Height);
            if (!string.IsNullOrEmpty(suspicionReason))
            {
                if (FaviconDiscoveryPreferences.EnableSvgDebugRenderDump)
                {
                    string dumpPath = TrySaveSvgDebugRender(svgBitmap, sourceUrl);
                    if (!string.IsNullOrWhiteSpace(dumpPath))
                    {
                        throw new InvalidOperationException("SVG conversion failed: " + suspicionReason + " Raw render saved: " + dumpPath);
                    }
                }

                throw new InvalidOperationException("SVG conversion failed: " + suspicionReason);
            }

            using (Bitmap analysisBitmap = CreateFittedCanvas(svgBitmap, SvgAnalysisCanvasSize, SvgAnalysisCanvasSize))
            {
                OpaqueMetrics metrics = MeasureOpaqueMetrics(analysisBitmap);

                if (!metrics.HasVisibleContent)
                {
                    throw new InvalidOperationException("SVG conversion failed: rendered bitmap is empty.");
                }

                if (metrics.OpaquePixelRatio < MinOpaquePixelRatio)
                {
                    throw new InvalidOperationException("SVG conversion failed: rendered bitmap is near-empty.");
                }

                if (metrics.ContentAreaRatio < MinContentAreaRatio)
                {
                    throw new InvalidOperationException("SVG conversion failed: rendered content is too small for icon usage.");
                }

                if (metrics.ContentAspectRatio > MaxContentAspectRatio)
                {
                    throw new InvalidOperationException("SVG conversion failed: rendered content aspect ratio is abnormal.");
                }

                if (LooksLikeBoundaryClipping(metrics, analysisBitmap.Width, analysisBitmap.Height))
                {
                    throw new InvalidOperationException("SVG conversion failed: rendered content appears clipped by canvas boundaries.");
                }
            }

            if (ShouldTrimTransparentMargins(sourceMetrics, svgBitmap.Width, svgBitmap.Height))
            {
                return CropToOpaqueBounds(svgBitmap, sourceMetrics.Bounds);
            }

            return new Bitmap(svgBitmap);
        }

        private static string GetSuspiciousSvgReason(OpaqueMetrics metrics, int width, int height)
        {
            if (!metrics.HasVisibleContent)
            {
                return "rendered bitmap is empty";
            }

            if (metrics.OpaquePixelRatio < MinOpaquePixelRatio)
            {
                return "rendered bitmap is near-empty";
            }

            if (metrics.ContentAreaRatio < MinContentAreaRatio)
            {
                return "rendered content is too small for icon usage";
            }

            if (metrics.ContentAspectRatio > MaxContentAspectRatio)
            {
                return "rendered content aspect ratio is abnormal";
            }

            if (LooksLikeBoundaryClipping(metrics, width, height))
            {
                return "rendered content appears clipped by canvas boundaries";
            }

            if (LooksLikeCornerAnchoredContent(metrics, width, height))
            {
                return "rendered content appears corner-anchored/cropped";
            }

            return null;
        }

        private static bool LooksLikeCornerAnchoredContent(OpaqueMetrics metrics, int width, int height)
        {
            if (!metrics.HasVisibleContent)
            {
                return false;
            }

            double centerX = metrics.Bounds.Left + (metrics.Bounds.Width / 2.0d);
            double centerY = metrics.Bounds.Top + (metrics.Bounds.Height / 2.0d);
            double normalizedOffsetX = Math.Abs(centerX - (width / 2.0d)) / Math.Max(1.0d, width / 2.0d);
            double normalizedOffsetY = Math.Abs(centerY - (height / 2.0d)) / Math.Max(1.0d, height / 2.0d);

            return normalizedOffsetX > MaxCenterOffsetSuspicionRatio
                || normalizedOffsetY > MaxCenterOffsetSuspicionRatio;
        }

        private static string TrySaveSvgDebugRender(Bitmap bitmap, string sourceUrl)
        {
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "FaviconExtractor", "svg-debug");
                Directory.CreateDirectory(folder);

                string safeHost = "unknown";
                Uri uri;
                if (!string.IsNullOrWhiteSpace(sourceUrl) && Uri.TryCreate(sourceUrl, UriKind.Absolute, out uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    safeHost = uri.Host.Replace(':', '_');
                }

                string file = safeHost + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                string path = Path.Combine(folder, file);
                bitmap.Save(path, ImageFormat.Png);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static OpaqueMetrics MeasureOpaqueMetrics(Bitmap bitmap)
        {
            Rectangle bounds = Rectangle.Empty;
            int opaquePixels = 0;
            bool hasVisible = false;
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = -1;
            int bottom = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A <= OpaqueAlphaThreshold)
                    {
                        continue;
                    }

                    hasVisible = true;
                    opaquePixels++;

                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }

            if (hasVisible)
            {
                bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }

            int totalPixels = Math.Max(1, bitmap.Width * bitmap.Height);
            double opaqueRatio = (double)opaquePixels / totalPixels;
            double contentAreaRatio = hasVisible
                ? (double)(bounds.Width * bounds.Height) / totalPixels
                : 0d;
            double aspect = hasVisible
                ? (double)Math.Max(bounds.Width, bounds.Height) / Math.Max(1, Math.Min(bounds.Width, bounds.Height))
                : 0d;

            return new OpaqueMetrics
            {
                HasVisibleContent = hasVisible,
                Bounds = bounds,
                OpaquePixelRatio = opaqueRatio,
                ContentAreaRatio = contentAreaRatio,
                ContentAspectRatio = aspect
            };
        }

        private static bool LooksLikeBoundaryClipping(OpaqueMetrics metrics, int width, int height)
        {
            if (!metrics.HasVisibleContent)
            {
                return true;
            }

            int leftMargin = metrics.Bounds.Left;
            int topMargin = metrics.Bounds.Top;
            int rightMargin = width - metrics.Bounds.Right;
            int bottomMargin = height - metrics.Bounds.Bottom;

            bool touchesLeft = leftMargin <= 1;
            bool touchesRight = rightMargin <= 1;
            bool touchesTop = topMargin <= 1;
            bool touchesBottom = bottomMargin <= 1;

            if (touchesLeft && rightMargin > (int)(width * OppositeMarginSuspicionRatio)) return true;
            if (touchesRight && leftMargin > (int)(width * OppositeMarginSuspicionRatio)) return true;
            if (touchesTop && bottomMargin > (int)(height * OppositeMarginSuspicionRatio)) return true;
            if (touchesBottom && topMargin > (int)(height * OppositeMarginSuspicionRatio)) return true;

            return false;
        }

        private static bool ShouldTrimTransparentMargins(OpaqueMetrics metrics, int width, int height)
        {
            if (!metrics.HasVisibleContent)
            {
                return false;
            }

            int leftMargin = metrics.Bounds.Left;
            int topMargin = metrics.Bounds.Top;
            int rightMargin = width - metrics.Bounds.Right;
            int bottomMargin = height - metrics.Bounds.Bottom;

            bool touchesBoundary = leftMargin <= 1 || topMargin <= 1 || rightMargin <= 1 || bottomMargin <= 1;
            if (touchesBoundary)
            {
                return false;
            }

            double horizontalTransparentRatio = (double)(leftMargin + rightMargin) / Math.Max(1, width);
            double verticalTransparentRatio = (double)(topMargin + bottomMargin) / Math.Max(1, height);
            return horizontalTransparentRatio >= ExcessMarginRatioForTrim || verticalTransparentRatio >= ExcessMarginRatioForTrim;
        }

        private static Bitmap CropToOpaqueBounds(Bitmap source, Rectangle bounds)
        {
            Rectangle safeBounds = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), bounds);
            if (safeBounds.Width <= 0 || safeBounds.Height <= 0)
            {
                return new Bitmap(source);
            }

            Bitmap cropped = new Bitmap(safeBounds.Width, safeBounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(cropped))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, safeBounds.Width, safeBounds.Height), safeBounds, GraphicsUnit.Pixel);
            }

            return cropped;
        }

        private static Bitmap CreateFittedCanvas(Bitmap source, int targetWidth, int targetHeight)
        {
            Bitmap canvas = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle destination = CalculateDestinationRectangle(source.Width, source.Height, targetWidth, targetHeight, true);
                graphics.DrawImage(source, destination);
            }

            return canvas;
        }

        private sealed class OpaqueMetrics
        {
            public bool HasVisibleContent { get; set; }
            public Rectangle Bounds { get; set; }
            public double OpaquePixelRatio { get; set; }
            public double ContentAreaRatio { get; set; }
            public double ContentAspectRatio { get; set; }
        }

        private static Bitmap DecodeIcoToBitmap(byte[] icoBytes)
        {
            using (MemoryStream stream = new MemoryStream(icoBytes))
            using (Icon icon = new Icon(stream, new Size(FaviconDiscoveryPreferences.NormalizedIconSize, FaviconDiscoveryPreferences.NormalizedIconSize)))
            using (Bitmap iconBitmap = icon.ToBitmap())
            {
                return new Bitmap(iconBitmap);
            }
        }

        private static Bitmap NormalizeBitmap(Bitmap source, bool forceResizeToTarget)
        {
            bool isSquare = source.Width > 0 && source.Height > 0 && source.Width == source.Height;
            if (!forceResizeToTarget && isSquare)
            {
                return CloneBitmap(source);
            }

            if (!forceResizeToTarget && !isSquare)
            {
                int squareSide = Math.Max(source.Width, source.Height);
                return PadToSquareWithoutResizing(source, squareSide);
            }

            int targetSize = FaviconDiscoveryPreferences.NormalizedIconSize;
            Bitmap canvas = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle destination = CalculateDestinationRectangle(source.Width, source.Height, targetSize, targetSize, forceResizeToTarget);
                graphics.DrawImage(source, destination);
            }

            return canvas;
        }

        private static Bitmap PadToSquareWithoutResizing(Bitmap source, int squareSide)
        {
            Bitmap canvas = new Bitmap(squareSide, squareSide, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int x = (squareSide - source.Width) / 2;
                int y = (squareSide - source.Height) / 2;
                graphics.DrawImage(source, new Rectangle(x, y, source.Width, source.Height));
            }

            return canvas;
        }

        private static Bitmap CloneBitmap(Bitmap source)
        {
            Bitmap clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(clone))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }

            return clone;
        }

        private static Rectangle CalculateDestinationRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, bool forceUpscale)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return new Rectangle(0, 0, targetWidth, targetHeight);
            }

            double scaleX = (double)targetWidth / sourceWidth;
            double scaleY = (double)targetHeight / sourceHeight;
            double scale = Math.Min(scaleX, scaleY);
            int sourceMaxSide = Math.Max(sourceWidth, sourceHeight);

            if (!forceUpscale
                && !FaviconDiscoveryPreferences.UpscaleSmallImagesDuringNormalization
                && scale > 1.0d
                && sourceMaxSide > FaviconDiscoveryPreferences.TinySourceUpscaleThreshold)
            {
                scale = 1.0d;
            }

            int scaledWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

            int x = (targetWidth - scaledWidth) / 2;
            int y = (targetHeight - scaledHeight) / 2;

            return new Rectangle(x, y, scaledWidth, scaledHeight);
        }

        private static bool IsSvg(string contentType, string sourceUrl, byte[] sourceBytes)
        {
            if (!string.IsNullOrWhiteSpace(contentType) && contentType.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl) && sourceUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefix = GetTextPrefix(sourceBytes, 512);
            return prefix.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsIco(string contentType, string sourceUrl, byte[] sourceBytes)
        {
            if (!string.IsNullOrWhiteSpace(contentType) && contentType.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl) && sourceUrl.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return sourceBytes.Length >= 4
                && sourceBytes[0] == 0x00
                && sourceBytes[1] == 0x00
                && sourceBytes[2] == 0x01
                && sourceBytes[3] == 0x00;
        }

        private static string GetTextPrefix(byte[] bytes, int maxByteCount)
        {
            int count = Math.Min(bytes.Length, maxByteCount);
            return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
        }

        private static T RunInStaThread<T>(Func<T> action, CancellationToken cancellationToken, TimeSpan timeout)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }

            T result = default(T);
            Exception captured = null;

            Thread thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            }));

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Stopwatch sw = Stopwatch.StartNew();
            while (!thread.Join(100))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (timeout > TimeSpan.Zero && sw.Elapsed >= timeout)
                {
                    throw new TimeoutException("STA-bound icon conversion timed out.");
                }
            }

            if (captured != null)
            {
                throw new InvalidOperationException("Failed to execute STA-bound operation.", captured);
            }

            return result;
        }
    }
}
