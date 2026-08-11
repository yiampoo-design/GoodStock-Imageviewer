using System.Collections.Generic;
using System.Linq;

namespace WpfApp1.Models
{
    /// <summary>
    /// A requested change to a metadata field. A field is only included when the
    /// user actually changed it (set to a value or intentionally cleared).
    /// </summary>
    public enum FieldAction
    {
        Unchanged,
        Set,
        Clear
    }

    public sealed class MetadataPatch
    {
        public FieldAction TitleAction { get; set; } = FieldAction.Unchanged;
        public string? Title { get; set; }

        public FieldAction DescriptionAction { get; set; } = FieldAction.Unchanged;
        public string? Description { get; set; }

        public FieldAction KeywordsAction { get; set; } = FieldAction.Unchanged;
        public List<string>? Keywords { get; set; }

        public FieldAction CreatorAction { get; set; } = FieldAction.Unchanged;
        public string? Creator { get; set; }

        public FieldAction CopyrightAction { get; set; } = FieldAction.Unchanged;
        public string? Copyright { get; set; }

        public FieldAction DateTakenAction { get; set; } = FieldAction.Unchanged;
        public string? DateTaken { get; set; }

        public bool HasChanges =>
            TitleAction != FieldAction.Unchanged ||
            DescriptionAction != FieldAction.Unchanged ||
            KeywordsAction != FieldAction.Unchanged ||
            CreatorAction != FieldAction.Unchanged ||
            CopyrightAction != FieldAction.Unchanged ||
            DateTakenAction != FieldAction.Unchanged;

        public List<string> ToExifToolArgs()
        {
            var args = new List<string>();

            if (TitleAction == FieldAction.Set && Title != null)
            {
                args.Add($"-XMP-dc:Title={Title}");
                args.Add($"-IPTC:ObjectName={Title}");
            }
            else if (TitleAction == FieldAction.Clear)
            {
                args.Add("-XMP-dc:Title=");
                args.Add("-IPTC:ObjectName=");
            }

            if (DescriptionAction == FieldAction.Set && Description != null)
            {
                args.Add($"-XMP-dc:Description={Description}");
                args.Add($"-IPTC:Caption-Abstract={Description}");
                args.Add($"-ImageDescription={Description}");
            }
            else if (DescriptionAction == FieldAction.Clear)
            {
                args.Add("-XMP-dc:Description=");
                args.Add("-IPTC:Caption-Abstract=");
                args.Add("-ImageDescription=");
            }

            if (KeywordsAction == FieldAction.Set && Keywords != null)
            {
                var joined = string.Join(", ", Keywords);
                args.Add($"-XMP-dc:Subject={joined}");
                args.Add($"-IPTC:Keywords={joined}");
            }
            else if (KeywordsAction == FieldAction.Clear)
            {
                args.Add("-XMP-dc:Subject=");
                args.Add("-IPTC:Keywords=");
            }

            if (CreatorAction == FieldAction.Set && Creator != null)
            {
                args.Add($"-XMP-dc:Creator={Creator}");
                args.Add($"-Artist={Creator}");
                args.Add($"-IPTC:By-line={Creator}");
            }
            else if (CreatorAction == FieldAction.Clear)
            {
                args.Add("-XMP-dc:Creator=");
                args.Add("-Artist=");
                args.Add("-IPTC:By-line=");
            }

            if (CopyrightAction == FieldAction.Set && Copyright != null)
            {
                args.Add($"-Copyright={Copyright}");
                args.Add($"-XMP-dc:Rights={Copyright}");
                args.Add($"-IPTC:CopyrightNotice={Copyright}");
            }
            else if (CopyrightAction == FieldAction.Clear)
            {
                args.Add("-Copyright=");
                args.Add("-XMP-dc:Rights=");
                args.Add("-IPTC:CopyrightNotice=");
            }

            if (DateTakenAction == FieldAction.Set && DateTaken != null)
                args.Add($"-DateTimeOriginal={DateTaken}");
            else if (DateTakenAction == FieldAction.Clear)
                args.Add("-DateTimeOriginal=");

            return args;
        }

        /// <summary>
        /// Fields this patch explicitly requests to change. Used so verification
        /// only checks the requested fields (unchanged fields are not verified).
        /// </summary>
        public List<string> RequestedFieldNames()
        {
            var names = new List<string>();
            if (TitleAction != FieldAction.Unchanged) names.Add("Title");
            if (DescriptionAction != FieldAction.Unchanged) names.Add("Description");
            if (KeywordsAction != FieldAction.Unchanged) names.Add("Keywords");
            if (CreatorAction != FieldAction.Unchanged) names.Add("Creator");
            if (CopyrightAction != FieldAction.Unchanged) names.Add("Copyright");
            if (DateTakenAction != FieldAction.Unchanged) names.Add("DateTaken");
            return names;
        }

        /// <summary>True if the user explicitly requested clearing the named field.</summary>
        public bool IsClearRequested(string fieldName) => fieldName switch
        {
            "Title" => TitleAction == FieldAction.Clear,
            "Description" => DescriptionAction == FieldAction.Clear,
            "Keywords" => KeywordsAction == FieldAction.Clear,
            "Creator" => CreatorAction == FieldAction.Clear,
            "Copyright" => CopyrightAction == FieldAction.Clear,
            "DateTaken" => DateTakenAction == FieldAction.Clear,
            _ => false,
        };

        /// <summary>Convenience: build a patch where a field is set from user input.</summary>
        public static MetadataPatch SetTitle(string? value) => new()
        {
            TitleAction = string.IsNullOrWhiteSpace(value) ? FieldAction.Clear : FieldAction.Set,
            Title = value,
        };

        public static MetadataPatch SetDescription(string? value) => new()
        {
            DescriptionAction = string.IsNullOrWhiteSpace(value) ? FieldAction.Clear : FieldAction.Set,
            Description = value,
        };

        public static MetadataPatch SetKeywords(List<string>? value) => new()
        {
            KeywordsAction = value == null || value.Count == 0 ? FieldAction.Clear : FieldAction.Set,
            Keywords = value,
        };

        public static MetadataPatch SetCreator(string? value) => new()
        {
            CreatorAction = string.IsNullOrWhiteSpace(value) ? FieldAction.Clear : FieldAction.Set,
            Creator = value,
        };

        public static MetadataPatch SetCopyright(string? value) => new()
        {
            CopyrightAction = string.IsNullOrWhiteSpace(value) ? FieldAction.Clear : FieldAction.Set,
            Copyright = value,
        };

        public static MetadataPatch SetDateTaken(string? value) => new()
        {
            DateTakenAction = string.IsNullOrWhiteSpace(value) ? FieldAction.Clear : FieldAction.Set,
            DateTaken = value,
        };
    }
}
