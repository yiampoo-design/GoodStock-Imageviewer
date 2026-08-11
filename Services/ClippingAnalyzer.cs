using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public static class ClippingAnalyzer
    {
        public static async Task<ClippingInfo> AnalyzeAsync(
            BitmapSource source,
            ClippingMode mode,
            int highlightThreshold = 250,
            int shadowThreshold = 5,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int w = formatted.PixelWidth;
                int h = formatted.PixelHeight;
                var pixels = new byte[w * h * 4];
                formatted.CopyPixels(pixels, w * 4, 0);

                int totalPixels = w * h;
                int highlightCount = 0;
                int shadowCount = 0;
                int rHi = 0, gHi = 0, bHi = 0;
                int rSh = 0, gSh = 0, bSh = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    ct.ThrowIfCancellationRequested();

                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    bool rHiClip = r >= highlightThreshold;
                    bool gHiClip = g >= highlightThreshold;
                    bool bHiClip = b >= highlightThreshold;
                    bool rShClip = r <= shadowThreshold;
                    bool gShClip = g <= shadowThreshold;
                    bool bShClip = b <= shadowThreshold;

                    if (rHiClip) rHi++;
                    if (gHiClip) gHi++;
                    if (bHiClip) bHi++;
                    if (rShClip) rSh++;
                    if (gShClip) gSh++;
                    if (bShClip) bSh++;

                    if (mode == ClippingMode.Luminance)
                    {
                        int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                        if (lum >= highlightThreshold) highlightCount++;
                        else if (lum <= shadowThreshold) shadowCount++;
                    }
                    else
                    {
                        if (rHiClip || gHiClip || bHiClip)
                            highlightCount++;
                        else if (rShClip || gShClip || bShClip)
                            shadowCount++;
                    }
                }

                return new ClippingInfo
                {
                    HighlightPixelCount = highlightCount,
                    ShadowPixelCount = shadowCount,
                    TotalPixels = totalPixels,
                    HighlightPercentage = totalPixels > 0 ? highlightCount * 100.0 / totalPixels : 0,
                    ShadowPercentage = totalPixels > 0 ? shadowCount * 100.0 / totalPixels : 0,
                    RedHighlightPixels = rHi,
                    GreenHighlightPixels = gHi,
                    BlueHighlightPixels = bHi,
                    RedShadowPixels = rSh,
                    GreenShadowPixels = gSh,
                    BlueShadowPixels = bSh,
                };
            }, ct);
        }

        public static WriteableBitmap GenerateOverlay(
            BitmapSource source,
            ClippingMode mode,
            int highlightThreshold = 250,
            int shadowThreshold = 5,
            double opacity = 0.7)
        {
            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = formatted.PixelWidth;
            int h = formatted.PixelHeight;
            var pixels = new byte[w * h * 4];
            formatted.CopyPixels(pixels, w * 4, 0);

            var overlayPixels = new byte[w * h * 4];
            byte alpha = (byte)(255 * opacity);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                bool isHighlight = false;
                bool isShadow = false;

                if (mode == ClippingMode.Luminance)
                {
                    int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    isHighlight = lum >= highlightThreshold;
                    isShadow = lum <= shadowThreshold;
                }
                else
                {
                    isHighlight = r >= highlightThreshold || g >= highlightThreshold || b >= highlightThreshold;
                    isShadow = r <= shadowThreshold || g <= shadowThreshold || b <= shadowThreshold;
                }

                if (isHighlight)
                {
                    overlayPixels[i] = 0;
                    overlayPixels[i + 1] = 0;
                    overlayPixels[i + 2] = 255;
                    overlayPixels[i + 3] = alpha;
                }
                else if (isShadow)
                {
                    overlayPixels[i] = 255;
                    overlayPixels[i + 1] = 0;
                    overlayPixels[i + 2] = 0;
                    overlayPixels[i + 3] = alpha;
                }
            }

            var overlay = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            overlay.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), overlayPixels, w * 4, 0);
            return overlay;
        }
    }
}
