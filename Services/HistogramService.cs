using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public static class HistogramService
    {
        public static async Task<HistogramData> ComputeAsync(BitmapSource source, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var formatted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = formatted.PixelWidth;
                int h = formatted.PixelHeight;
                var pixels = new byte[w * h * 4];
                formatted.CopyPixels(pixels, w * 4, 0);

                var data = new HistogramData();
                int totalPixels = w * h;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    ct.ThrowIfCancellationRequested();

                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    data.Red[r]++;
                    data.Green[g]++;
                    data.Blue[b]++;

                    int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    if (lum > 255) lum = 255;
                    data.Luminance[lum]++;
                }

                for (int i = 0; i < 256; i++)
                {
                    if (data.Red[i] > data.MaxRed) data.MaxRed = data.Red[i];
                    if (data.Green[i] > data.MaxGreen) data.MaxGreen = data.Green[i];
                    if (data.Blue[i] > data.MaxBlue) data.MaxBlue = data.Blue[i];
                    if (data.Luminance[i] > data.MaxLuminance) data.MaxLuminance = data.Luminance[i];
                }

                int highlightPixels = 0;
                int shadowPixels = 0;
                for (int i = 250; i <= 255; i++) highlightPixels += data.Luminance[i];
                for (int i = 0; i <= 5; i++) shadowPixels += data.Luminance[i];

                data.OverexposedPercentage = totalPixels > 0 ? highlightPixels * 100.0 / totalPixels : 0;
                data.UnderexposedPercentage = totalPixels > 0 ? shadowPixels * 100.0 / totalPixels : 0;

                return data;
            }, ct);
        }

        public static double[] DownsampleForDisplay(int[] histogram, int bins, int maxBarHeight)
        {
            if (histogram == null || histogram.Length == 0)
                return new double[bins];

            int binsPerBar = 256 / bins;
            var result = new double[bins];
            double maxVal = 0;

            for (int b = 0; b < bins; b++)
            {
                for (int i = b * binsPerBar; i < (b + 1) * binsPerBar && i < 256; i++)
                    result[b] += histogram[i];
                if (result[b] > maxVal) maxVal = result[b];
            }

            if (maxVal > 0)
                for (int i = 0; i < bins; i++)
                    result[i] = result[i] / maxVal * maxBarHeight;

            return result;
        }
    }
}
