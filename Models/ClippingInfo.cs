namespace WpfApp1.Models
{
    public enum ClippingMode
    {
        RgbChannels,
        Luminance
    }

    public sealed class ClippingInfo
    {
        public double HighlightPercentage { get; set; }
        public double ShadowPercentage { get; set; }
        public int HighlightPixelCount { get; set; }
        public int ShadowPixelCount { get; set; }
        public int TotalPixels { get; set; }

        public int RedHighlightPixels { get; set; }
        public int GreenHighlightPixels { get; set; }
        public int BlueHighlightPixels { get; set; }
        public int RedShadowPixels { get; set; }
        public int GreenShadowPixels { get; set; }
        public int BlueShadowPixels { get; set; }

        public double RedHighlightPercentage => TotalPixels > 0 ? RedHighlightPixels * 100.0 / TotalPixels : 0;
        public double GreenHighlightPercentage => TotalPixels > 0 ? GreenHighlightPixels * 100.0 / TotalPixels : 0;
        public double BlueHighlightPercentage => TotalPixels > 0 ? BlueHighlightPixels * 100.0 / TotalPixels : 0;
        public double RedShadowPercentage => TotalPixels > 0 ? RedShadowPixels * 100.0 / TotalPixels : 0;
        public double GreenShadowPercentage => TotalPixels > 0 ? GreenShadowPixels * 100.0 / TotalPixels : 0;
        public double BlueShadowPercentage => TotalPixels > 0 ? BlueShadowPixels * 100.0 / TotalPixels : 0;
    }
}
