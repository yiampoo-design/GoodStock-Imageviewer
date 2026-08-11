using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfApp1.Services
{
    public static class ImageDecodeService
    {
        public static async Task<BitmapSource?> LoadThumbnailAsync(string filePath, int maxDimension = 200, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = CreateDecoder(filePath, fs);
                    if (decoder == null || decoder.Frames.Count == 0) return null;

                    var frame = decoder.Frames[0];
                    var scaled = ScaleToMax(frame, maxDimension);
                    scaled.Freeze();
                    return scaled;
                }
                catch { return null; }
            }, ct);
        }

        public static async Task<BitmapSource?> LoadPreviewAsync(string filePath, int maxDimension = 1200, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = CreateDecoder(filePath, fs);
                    if (decoder == null || decoder.Frames.Count == 0) return null;

                    var frame = decoder.Frames[0];
                    var scaled = ScaleToMax(frame, maxDimension);
                    scaled.Freeze();
                    return scaled;
                }
                catch { return null; }
            }, ct);
        }

        public static async Task<BitmapSource?> LoadFullResolutionAsync(string filePath, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = CreateDecoder(filePath, fs);
                    if (decoder == null || decoder.Frames.Count == 0) return null;

                    var frame = decoder.Frames[0];
                    var formatted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                    formatted.Freeze();
                    return formatted;
                }
                catch { return null; }
            }, ct);
        }

        public static async Task<FormatConvertedBitmap?> LoadAnalysisBitmapAsync(string filePath, int maxDimension = 512, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = CreateDecoder(filePath, fs);
                    if (decoder == null || decoder.Frames.Count == 0) return null;

                    var frame = decoder.Frames[0];
                    var scaled = ScaleToMax(frame, maxDimension);
                    var formatted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                    formatted.Freeze();
                    return formatted;
                }
                catch { return null; }
            }, ct);
        }

        public static bool GetImageDimensions(string filePath, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = CreateDecoder(filePath, fs);
                if (decoder?.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static BitmapDecoder? CreateDecoder(string filePath, Stream stream)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                ".png" => new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                ".tiff" or ".tif" => new TiffBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                ".bmp" => new BmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                ".gif" => new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                ".wdp" or ".wmp" => new WmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                _ => null,
            };
        }

        private static BitmapSource ScaleToMax(BitmapSource source, int maxDimension)
        {
            double scale = Math.Min((double)maxDimension / source.PixelWidth, (double)maxDimension / source.PixelHeight);
            if (scale >= 1) return source;
            var target = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            target.Freeze();
            return target;
        }
    }
}
