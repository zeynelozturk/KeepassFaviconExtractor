using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FaviconExtractor
{
    internal static class WebpDecoder
    {
        public static bool LooksLikeWebp(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return false;
            }

            return data[0] == (byte)'R'
                && data[1] == (byte)'I'
                && data[2] == (byte)'F'
                && data[3] == (byte)'F'
                && data[8] == (byte)'W'
                && data[9] == (byte)'E'
                && data[10] == (byte)'B'
                && data[11] == (byte)'P';
        }

        public static Bitmap DecodeToBitmap(byte[] webpBytes)
        {
            if (webpBytes == null || webpBytes.Length == 0)
            {
                throw new ArgumentException("WEBP source bytes are empty.", nameof(webpBytes));
            }

            int width;
            int height;
            if (WebPGetInfo(webpBytes, new UIntPtr((uint)webpBytes.Length), out width, out height) == 0)
            {
                throw new InvalidOperationException("WEBP header parse failed.");
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("WEBP reported invalid dimensions.");
            }

            checked
            {
                int stride = width * 4;
                int bufferSize = stride * height;

                byte[] decoded = new byte[bufferSize];
                GCHandle handle = GCHandle.Alloc(decoded, GCHandleType.Pinned);
                try
                {
                    IntPtr pinned = handle.AddrOfPinnedObject();
                    IntPtr result = WebPDecodeBGRAInto(
                        webpBytes,
                        new UIntPtr((uint)webpBytes.Length),
                        pinned,
                        new UIntPtr((uint)bufferSize),
                        stride);

                    if (result == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("WEBP pixel decode failed.");
                    }
                }
                catch (DllNotFoundException ex)
                {
                    throw new InvalidOperationException("WEBP decoder native library (libwebp.dll) not found.", ex);
                }
                catch (BadImageFormatException ex)
                {
                    throw new InvalidOperationException("WEBP decoder native library (libwebp.dll) architecture is incompatible.", ex);
                }
                catch (EntryPointNotFoundException ex)
                {
                    throw new InvalidOperationException("WEBP decoder native library (libwebp.dll) is missing required exports.", ex);
                }
                catch (SEHException ex)
                {
                    throw new InvalidOperationException("WEBP decoder native call failed unexpectedly.", ex);
                }
                finally
                {
                    handle.Free();
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Rectangle rect = new Rectangle(0, 0, width, height);
                BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(decoded, 0, data.Scan0, bufferSize);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return bitmap;
            }
        }

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPGetInfo(byte[] data, UIntPtr data_size, out int width, out int height);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPDecodeBGRAInto(
            byte[] data,
            UIntPtr data_size,
            IntPtr output_buffer,
            UIntPtr output_buffer_size,
            int output_stride);
    }
}
