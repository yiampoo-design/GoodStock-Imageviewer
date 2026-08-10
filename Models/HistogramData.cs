namespace WpfApp1.Models
{
    public sealed class HistogramData
    {
        public int[] Red { get; set; } = new int[256];
        public int[] Green { get; set; } = new int[256];
        public int[] Blue { get; set; } = new int[256];
        public int[] Luminance { get; set; } = new int[256];

        public int MaxRed { get; set; }
        public int MaxGreen { get; set; }
        public int MaxBlue { get; set; }
        public int MaxLuminance { get; set; }
        public int MaxAll => System.Math.Max(MaxRed, System.Math.Max(MaxGreen, System.Math.Max(MaxBlue, MaxLuminance)));

        public double OverexposedPercentage { get; set; }
        public double UnderexposedPercentage { get; set; }
    }
}
