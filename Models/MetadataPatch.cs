using System.Collections.Generic;

namespace WpfApp1.Models
{
    public sealed class MetadataPatch
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<string>? Keywords { get; set; }
        public string? Creator { get; set; }
        public string? Copyright { get; set; }
        public string? DateTaken { get; set; }

        public bool HasChanges =>
            Title != null || Description != null || Keywords != null ||
            Creator != null || Copyright != null || DateTaken != null;

        public List<string> ToExifToolArgs()
        {
            var args = new List<string>();

            if (Title != null)
            {
                args.Add($"-XMP-dc:Title={Title}");
                args.Add($"-IPTC:ObjectName={Title}");
            }

            if (Description != null)
            {
                args.Add($"-XMP-dc:Description={Description}");
                args.Add($"-IPTC:Caption-Abstract={Description}");
                args.Add($"-ImageDescription={Description}");
            }

            if (Keywords != null)
            {
                var joined = string.Join(", ", Keywords);
                args.Add($"-XMP-dc:Subject={joined}");
                args.Add($"-IPTC:Keywords={joined}");
            }

            if (Creator != null)
            {
                args.Add($"-XMP-dc:Creator={Creator}");
                args.Add($"-Artist={Creator}");
                args.Add($"-IPTC:By-line={Creator}");
            }

            if (Copyright != null)
            {
                args.Add($"-Copyright={Copyright}");
                args.Add($"-XMP-dc:Rights={Copyright}");
                args.Add($"-IPTC:CopyrightNotice={Copyright}");
            }

            if (DateTaken != null)
                args.Add($"-DateTimeOriginal={DateTaken}");

            return args;
        }
    }
}
