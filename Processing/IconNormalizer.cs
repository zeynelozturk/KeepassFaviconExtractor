using System;
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
        public static byte[] NormalizeToPng(byte[] sourceBytes, string contentType, string sourceUrl)
        {
            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                throw new ArgumentException("Source icon bytes are empty.", nameof(sourceBytes));
            }

            using (Bitmap sourceBitmap = DecodeToBitmap(sourceBytes, contentType, sourceUrl))
            using (Bitmap normalizedBitmap = NormalizeBitmap(sourceBitmap))
            using (MemoryStream outputStream = new MemoryStream())
            {
                normalizedBitmap.Save(outputStream, ImageFormat.Png);
                return outputStream.ToArray();
            }
        }

        private static Bitmap DecodeToBitmap(byte[] sourceBytes, string contentType, string sourceUrl)
        {
            if (IsSvg(contentType, sourceUrl, sourceBytes))
            {
                return DecodeSvgToBitmap(sourceBytes);
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

        private static Bitmap DecodeSvgToBitmap(byte[] svgBytes)
        {
            return RunInStaThread(delegate
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
            });
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

        private static Bitmap NormalizeBitmap(Bitmap source)
        {
            if (source.Width > 0 && source.Height > 0 && source.Width == source.Height)
            {
                return CloneBitmap(source);
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

                Rectangle destination = CalculateDestinationRectangle(source.Width, source.Height, targetSize, targetSize);
                graphics.DrawImage(source, destination);
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

        private static Rectangle CalculateDestinationRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return new Rectangle(0, 0, targetWidth, targetHeight);
            }

            double scaleX = (double)targetWidth / sourceWidth;
            double scaleY = (double)targetHeight / sourceHeight;
            double scale = Math.Min(scaleX, scaleY);
            int sourceMaxSide = Math.Max(sourceWidth, sourceHeight);

            if (!FaviconDiscoveryPreferences.UpscaleSmallImagesDuringNormalization
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

        private static T RunInStaThread<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
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
            thread.Join();

            if (captured != null)
            {
                throw new InvalidOperationException("Failed to execute STA-bound operation.", captured);
            }

            return result;
        }
    }
}
