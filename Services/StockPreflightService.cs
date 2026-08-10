using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public static class StockPreflightService
    {
        private static readonly HashSet<string> AcceptedFormats = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".tiff", ".tif", ".png" };

        private const int MinDimension = 2000;
        private const double MinMegapixels = 4.0;

        public static PreflightReport RunCheck(PhotoMetadata meta)
        {
            var report = new PreflightReport { FilePath = meta.FilePath };

            report.Checks.Add(CheckDimensions(meta));
            report.Checks.Add(CheckFormat(meta));
            report.Checks.Add(CheckIccProfile(meta));
            report.Checks.Add(CheckMetadataCompleteness(meta));
            report.Checks.Add(CheckGpsPrivacy(meta));
            report.Checks.Add(CheckDuplicateKeywords(meta));

            return report;
        }

        private static PreflightCheck CheckDimensions(PhotoMetadata meta)
        {
            if (meta.Width < MinDimension || meta.Height < MinDimension)
                return new PreflightCheck
                {
                    Name = "Dimensions",
                    Status = PreflightStatus.Warning,
                    Message = $"{meta.Width}x{meta.Height} ({meta.Megapixels:F1} MP) — minimum {MinDimension}x{MinDimension}",
                };

            return new PreflightCheck
            {
                Name = "Dimensions",
                Status = PreflightStatus.Ready,
                Message = $"{meta.Width}x{meta.Height} ({meta.Megapixels:F1} MP)",
            };
        }

        private static PreflightCheck CheckFormat(PhotoMetadata meta)
        {
            var ext = Path.GetExtension(meta.FilePath);
            if (AcceptedFormats.Contains(ext))
                return new PreflightCheck { Name = "Format", Status = PreflightStatus.Ready, Message = $"{ext.ToUpper().TrimStart('.')} is accepted" };

            return new PreflightCheck
            {
                Name = "Format",
                Status = PreflightStatus.NeedsAttention,
                Message = $"{ext} is not a standard stock format",
                Details = $"Accepted: {string.Join(", ", AcceptedFormats.Select(f => f.ToUpper().TrimStart('.')))}",
            };
        }

        private static PreflightCheck CheckIccProfile(PhotoMetadata meta)
        {
            if (!string.IsNullOrEmpty(meta.IccProfile))
                return new PreflightCheck { Name = "ICC Profile", Status = PreflightStatus.Ready, Message = meta.IccProfile };

            return new PreflightCheck
            {
                Name = "ICC Profile",
                Status = PreflightStatus.Warning,
                Message = "No ICC profile detected",
                Details = "Stock agencies prefer embedded color profiles (sRGB, Adobe RGB).",
            };
        }

        private static PreflightCheck CheckMetadataCompleteness(PhotoMetadata meta)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(meta.Title)) missing.Add("Title");
            if (string.IsNullOrWhiteSpace(meta.Description)) missing.Add("Description");
            if (meta.Keywords.Count == 0) missing.Add("Keywords");
            if (string.IsNullOrWhiteSpace(meta.Creator)) missing.Add("Creator");
            if (string.IsNullOrWhiteSpace(meta.Copyright)) missing.Add("Copyright");

            if (missing.Count == 0)
                return new PreflightCheck { Name = "Metadata", Status = PreflightStatus.Ready, Message = "All required fields present" };

            if (missing.Count <= 2)
                return new PreflightCheck
                {
                    Name = "Metadata",
                    Status = PreflightStatus.Warning,
                    Message = $"Missing: {string.Join(", ", missing)}",
                };

            return new PreflightCheck
            {
                Name = "Metadata",
                Status = PreflightStatus.NeedsAttention,
                Message = $"Missing {missing.Count} fields: {string.Join(", ", missing)}",
            };
        }

        private static PreflightCheck CheckGpsPrivacy(PhotoMetadata meta)
        {
            if (meta.GpsLatitude != null || meta.GpsLongitude != null)
                return new PreflightCheck
                {
                    Name = "GPS Privacy",
                    Status = PreflightStatus.Warning,
                    Message = "GPS coordinates embedded — consider removing for privacy",
                };

            return new PreflightCheck { Name = "GPS Privacy", Status = PreflightStatus.Ready, Message = "No GPS data" };
        }

        private static PreflightCheck CheckDuplicateKeywords(PhotoMetadata meta)
        {
            if (meta.Keywords.Count == 0)
                return new PreflightCheck { Name = "Keywords", Status = PreflightStatus.Ready, Message = "No keywords to check" };

            var lower = meta.Keywords.Select(k => k.Trim().ToLowerInvariant()).ToList();
            var dupes = lower.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            if (dupes.Count == 0)
                return new PreflightCheck { Name = "Keywords", Status = PreflightStatus.Ready, Message = $"{meta.Keywords.Count} unique keywords" };

            return new PreflightCheck
            {
                Name = "Keywords",
                Status = PreflightStatus.Warning,
                Message = $"{dupes.Count} duplicate keyword(s): {string.Join(", ", dupes.Take(3))}",
            };
        }
    }
}
