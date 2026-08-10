using System.Collections.Generic;

namespace WpfApp1.Models
{
    public enum PreflightStatus
    {
        Ready,
        Warning,
        NeedsAttention
    }

    public sealed class PreflightCheck
    {
        public string Name { get; set; } = "";
        public PreflightStatus Status { get; set; }
        public string Message { get; set; } = "";
        public string? Details { get; set; }
    }

    public sealed class PreflightReport
    {
        public string FilePath { get; set; } = "";
        public List<PreflightCheck> Checks { get; set; } = new();
        public PreflightStatus OverallStatus => Checks.Exists(c => c.Status == PreflightStatus.NeedsAttention)
            ? PreflightStatus.NeedsAttention
            : Checks.Exists(c => c.Status == PreflightStatus.Warning)
                ? PreflightStatus.Warning
                : PreflightStatus.Ready;
    }
}
