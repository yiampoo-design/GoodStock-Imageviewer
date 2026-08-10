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
    }
}
