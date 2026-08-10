using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1.Services
{
    public sealed class FileOperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int FilesProcessed { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public interface IFileOperationService : IDisposable
    {
        Task<FileOperationResult> CopyFilesAsync(IEnumerable<string> sourcePaths, string destinationFolder, CancellationToken ct = default);
        Task<FileOperationResult> MoveFilesAsync(IEnumerable<string> sourcePaths, string destinationFolder, CancellationToken ct = default);
        Task<FileOperationResult> DeleteToRecycleBinAsync(IEnumerable<string> paths, CancellationToken ct = default);
        Task<FileOperationResult> RenameFileAsync(string filePath, string newName, CancellationToken ct = default);
    }
}
