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

            meta.FileType = GetString(element, "FileType") ?? GetExtensionGroup(element);
            meta.Title = GetString(element, "XMP-dc:Title") ?? GetString(element, "IPTC:ObjectName");
            meta.Description = GetString(element, "XMP-dc:Description") ?? GetString(element, "IPTC:Caption-Abstract") ?? GetString(element, "ImageDescription");

            var subject = GetString(element, "XMP-dc:Subject") ?? GetString(element, "IPTC:Keywords");
            if (!string.IsNullOrEmpty(subject))
                meta.Keywords = new List<string>(subject.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries));

            meta.Creator = GetString(element, "XMP-dc:Creator") ?? GetString(element, "Artist") ?? GetString(element, "IPTC:By-line");
            meta.Copyright = GetString(element, "Copyright") ?? GetString(element, "XMP-dc:Rights") ?? GetString(element, "IPTC:CopyrightNotice");
            meta.Make = GetString(element, "Make") ?? GetString(element, "EXIF:Make");
            meta.Model = GetString(element, "Model") ?? GetString(element, "EXIF:Model");
            meta.Lens = GetString(element, "LensModel") ?? GetString(element, "EXIF:LensModel");
            meta.IccProfile = GetString(element, "ICC_Profile:ProfileDescription") ?? GetString(element, "ICC_Profile");

            var dateStr = GetString(element, "DateTimeOriginal") ?? GetString(element, "EXIF:DateTimeOriginal") ?? GetString(element, "DateTime");
            if (DateTime.TryParse(dateStr, out var dt)) meta.DateTaken = dt;

            var modStr = GetString(element, "File:FileModifyDate") ?? GetString(element, "ModifyDate");
            if (DateTime.TryParse(modStr, out var modDt)) meta.DateModified = modDt;

            if (TryGetDouble(element, "GPSLatitude", out var lat)) meta.GpsLatitude = lat;
            if (TryGetDouble(element, "GPSLongitude", out var lon)) meta.GpsLongitude = lon;

            if (int.TryParse(GetString(element, "ImageWidth") ?? GetString(element, "EXIF:ImageWidth"), out var w))
                meta.Width = w;
            if (int.TryParse(GetString(element, "ImageHeight") ?? GetString(element, "EXIF:ImageHeight"), out var h))
                meta.Height = h;

            meta.Orientation = GetString(element, "Orientation") ?? GetString(element, "EXIF:Orientation");
            meta.Rating = GetString(element, "Rating") ?? GetString(element, "XMP:Rating");
            meta.ColorSpace = GetString(element, "ColorSpace") ?? GetString(element, "EXIF:ColorSpace");

            return meta;
        }

        private static string? GetString(JsonElement element, string tag)
        {
            if (element.TryGetProperty(tag, out var val))
                return ExtractString(val);

            foreach (var prefix in new[] { "EXIF:", "XMP:", "IPTC:", "File:" })
            {
                if (element.TryGetProperty(prefix + tag, out var grouped))
                    return ExtractString(grouped);
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
            return val.ToString();
        }

        private static bool TryGetDouble(JsonElement element, string tag, out double value)
        {
            value = 0;
            if (element.TryGetProperty(tag, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number) { value = val.GetDouble(); return true; }
                if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(), out value)) return true;
            }
            return false;
        }

        private static string? GetExtensionGroup(JsonElement element)
        {
            if (element.TryGetProperty("File:FileType", out var ft) && ft.ValueKind == JsonValueKind.String)
                return ft.GetString();
            return null;
        }
    }
}
