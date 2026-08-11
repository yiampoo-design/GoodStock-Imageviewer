using System;
using System.Threading;
using System.Threading.Tasks;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public interface IMetadataService : IDisposable
    {
        Task<PhotoMetadata?> ReadMetadataAsync(string filePath, CancellationToken ct = default);
        Task<MetadataWriteResult> WriteMetadataAsync(string filePath, MetadataPatch patch, CancellationToken ct = default);
        Task<bool> IsAvailableAsync();
        void InvalidateCache(string filePath);
        string? ExifToolPath { get; }
    }
}
