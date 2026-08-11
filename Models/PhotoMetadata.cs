using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WpfApp1.Models
{
    public sealed class PhotoMetadata
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public string? FileType { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Megapixels => Width * Height / 1_000_000.0;

        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<string> Keywords { get; set; } = new();
        public string? Creator { get; set; }
        public string? Copyright { get; set; }
        public DateTime? DateTaken { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Lens { get; set; }
        public string? FocalLength { get; set; }
        public string? FNumber { get; set; }
        public string? ExposureTime { get; set; }
        public string? Iso { get; set; }
        public string? IccProfile { get; set; }

        public double? GpsLatitude { get; set; }
        public double? GpsLongitude { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }

        public string? Rating { get; set; }
        public string? ColorSpace { get; set; }
        public string? Orientation { get; set; }

        public DateTime? DateModified { get; set; }
        public DateTime? DateDigitized { get; set; }

        public string? RawJson { get; set; }

        public static PhotoMetadata FromExifToolJson(string filePath, JsonElement element)
        {
            var meta = new PhotoMetadata
            {
                FilePath = filePath,
                FileName = System.IO.Path.GetFileName(filePath),
                RawJson = element.GetRawText(),
            };

            if (System.IO.File.Exists(filePath))
            {
                var fi = new System.IO.FileInfo(filePath);
                meta.FileSizeBytes = fi.Length;
            }

            meta.FileType = ResolveTag(element, "FileType", "File:FileType");
            meta.Title = ResolveTag(element, "XMP-dc:Title", "IPTC:ObjectName");
            meta.Description = ResolveTag(element, "XMP-dc:Description", "IPTC:Caption-Abstract", "ImageDescription", "IFD0:ImageDescription");
            meta.Creator = ResolveTag(element, "XMP-dc:Creator", "Artist", "IFD0:Artist", "IPTC:By-line");
            meta.Copyright = ResolveTag(element, "Copyright", "IFD0:Copyright", "XMP-dc:Rights", "IPTC:CopyrightNotice");
            meta.Make = ResolveTag(element, "IFD0:Make", "ExifIFD:Make");
            meta.Model = ResolveTag(element, "IFD0:Model", "ExifIFD:Model");
            meta.Lens = ResolveTag(element, "ExifIFD:LensModel");
            meta.FocalLength = ResolveTag(element, "ExifIFD:FocalLength");
            meta.FNumber = ResolveTag(element, "ExifIFD:FNumber");
            meta.ExposureTime = ResolveTag(element, "ExifIFD:ExposureTime");
            meta.Iso = ResolveTag(element, "ExifIFD:ISO");
            meta.IccProfile = ResolveTag(element, "ICC_Profile:ProfileDescription", "ICC_Profile");

            var subject = ResolveTag(element, "XMP-dc:Subject", "IPTC:Keywords");
            if (!string.IsNullOrEmpty(subject))
                meta.Keywords = new List<string>(subject.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries));

            var dateStr = ResolveTag(element, "ExifIFD:DateTimeOriginal", "IFD0:DateTime", "IFD0:ModifyDate");
            if (DateTime.TryParse(dateStr, out var dt)) meta.DateTaken = dt;

            var modStr = ResolveTag(element, "File:FileModifyDate");
            if (DateTime.TryParse(modStr, out var modDt)) meta.DateModified = modDt;

            var digStr = ResolveTag(element, "ExifIFD:DateTimeDigitized");
            if (DateTime.TryParse(digStr, out var digDt)) meta.DateDigitized = digDt;

            var latStr = ResolveTag(element, "GPS:GPSLatitude#", "GPSLatitude#", "GPS:GPSLatitude", "GPSLatitude");
            if (latStr != null && double.TryParse(latStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var latSigned))
            {
                meta.GpsLatitude = latSigned;
            }
            else if (latStr != null)
            {
                var latVal = ParseDmsToDecimal(latStr);
                if (latVal != null)
                {
                    var latRef = ResolveTag(element, "GPS:GPSLatitudeRef", "GPSLatitudeRef");
                    meta.GpsLatitude = string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase) ? -latVal.Value : latVal.Value;
                }
            }

            var lonStr = ResolveTag(element, "GPS:GPSLongitude#", "GPSLongitude#", "GPS:GPSLongitude", "GPSLongitude");
            if (lonStr != null && double.TryParse(lonStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lonSigned))
            {
                meta.GpsLongitude = lonSigned;
            }
            else if (lonStr != null)
            {
                var lonVal = ParseDmsToDecimal(lonStr);
                if (lonVal != null)
                {
                    var lonRef = ResolveTag(element, "GPS:GPSLongitudeRef", "GPSLongitudeRef");
                    meta.GpsLongitude = string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase) ? -lonVal.Value : lonVal.Value;
                }
            }

            if (int.TryParse(ResolveTag(element, "IFD0:ImageWidth", "ExifIFD:ImageWidth"), out var w))
                meta.Width = w;
            if (int.TryParse(ResolveTag(element, "IFD0:ImageHeight", "ExifIFD:ImageHeight"), out var h))
                meta.Height = h;

            meta.Orientation = ResolveTag(element, "IFD0:Orientation", "ExifIFD:Orientation");
            meta.Rating = ResolveTag(element, "XMP:Rating", "XMP-xmp:Rating");
            meta.ColorSpace = ResolveTag(element, "ExifIFD:ColorSpace");

            return meta;
        }

        private static string? ResolveTag(JsonElement element, params string[] tags)
        {
            foreach (var tag in tags)
            {
                if (element.TryGetProperty(tag, out var val))
                {
                    var s = ExtractString(val);
                    if (s != null) return s;
                }
            }
            return null;
        }

        private static string? ExtractString(JsonElement val)
        {
            if (val.ValueKind == JsonValueKind.String) return val.GetString();
            if (val.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in val.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) parts.Add(item.GetString()!);
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            if (val.ValueKind == JsonValueKind.Number) return val.ToString();
            return val.ToString();
        }

        private static bool TryGetDouble(JsonElement element, string tag, out double value)
        {
            value = 0;
            if (element.TryGetProperty(tag, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number) { value = val.GetDouble(); return true; }
                if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            }
            return false;
        }

        private static double? ParseDmsToDecimal(string dms)
        {
            if (string.IsNullOrWhiteSpace(dms)) return null;

            if (double.TryParse(dms, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var direct))
                return direct;

            var match = System.Text.RegularExpressions.Regex.Match(dms,
                @"(\d+(?:\.\d+)?)\s*deg\s+(\d+(?:\.\d+)?)['\u2032]\s*(\d+(?:\.\d+)?)[\u2033""\u2033]?\s*([NSEW]?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return null;

            var deg = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var min = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var sec = double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

            var decimalDeg = deg + min / 60.0 + sec / 3600.0;
            return decimalDeg;
        }
    }
}
