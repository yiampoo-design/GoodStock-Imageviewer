using System.Collections.Generic;

namespace WpfApp1.Models
{
    public sealed class MetadataWriteResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public PhotoMetadata? VerifiedMetadata { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
