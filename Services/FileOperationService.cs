using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace WpfApp1.Services
{
    public sealed class FileOperationService : IFileOperationService
    {
        private bool _disposed;

        public async Task<FileOperationResult> CopyFilesAsync(IEnumerable<string> sourcePaths, string destinationFolder, CancellationToken ct = default)
        {
            var result = new FileOperationResult();
            Directory.CreateDirectory(destinationFolder);

            foreach (var source in sourcePaths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(source)) { result.Errors.Add($"{source}: not found"); continue; }
                    var dest = Path.Combine(destinationFolder, Path.GetFileName(source));
                    dest = GetUniquePath(dest);
                    await Task.Run(() => File.Copy(source, dest, false), ct);
                    result.FilesProcessed++;
                }
                catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public async Task<FileOperationResult> MoveFilesAsync(IEnumerable<string> sourcePaths, string destinationFolder, CancellationToken ct = default)
        {
            var result = new FileOperationResult();
            Directory.CreateDirectory(destinationFolder);

            foreach (var source in sourcePaths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(source)) { result.Errors.Add($"{source}: not found"); continue; }
                    var dest = Path.Combine(destinationFolder, Path.GetFileName(source));
                    dest = GetUniquePath(dest);
                    await Task.Run(() => File.Move(source, dest), ct);
                    result.FilesProcessed++;
                }
                catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public async Task<FileOperationResult> CopyDirectoryAsync(string sourceDir, string destinationFolder, CancellationToken ct = default)
        {
            var result = new FileOperationResult();
            try
            {
                if (!Directory.Exists(sourceDir)) { result.Errors.Add($"{sourceDir}: not found"); return result; }
                Directory.CreateDirectory(destinationFolder);

                var dest = Path.Combine(destinationFolder, Path.GetFileName(sourceDir));

                var sourceFull = Path.GetFullPath(sourceDir).TrimEnd('\\', '/');
                var destFull = Path.GetFullPath(dest).TrimEnd('\\', '/');
                if (destFull.StartsWith(sourceFull + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("Cannot copy a folder into itself.");
                    return result;
                }

                dest = GetUniqueDirectoryPath(dest);
                await Task.Run(() => CopyDirectoryRecursive(sourceDir, dest, ct), ct);
                result.FilesProcessed++;
            }
            catch (OperationCanceledException) { result.Errors.Add("Operation cancelled"); }
            catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(sourceDir)}: {ex.Message}"); }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public async Task<FileOperationResult> MoveDirectoryAsync(string sourceDir, string destinationFolder, CancellationToken ct = default)
        {
            var result = new FileOperationResult();
            try
            {
                if (!Directory.Exists(sourceDir)) { result.Errors.Add($"{sourceDir}: not found"); return result; }
                Directory.CreateDirectory(destinationFolder);

                // Same-folder "move" (cut+paste into the same parent after no
                // real change) must not fail when the target already exists.
                var dest = Path.Combine(destinationFolder, Path.GetFileName(sourceDir));

                var sourceFull = Path.GetFullPath(sourceDir).TrimEnd('\\', '/');
                var destFull = Path.GetFullPath(dest).TrimEnd('\\', '/');
                if (destFull.StartsWith(sourceFull + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("Cannot move a folder into itself.");
                    return result;
                }

                dest = GetUniqueDirectoryPath(dest);
                await Task.Run(() => Directory.Move(sourceDir, dest), ct);
                result.FilesProcessed++;
            }
            catch (OperationCanceledException) { result.Errors.Add("Operation cancelled"); }
            catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(sourceDir)}: {ex.Message}"); }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public async Task<FileOperationResult> DeleteToRecycleBinAsync(IEnumerable<string> paths, CancellationToken ct = default)
        {
            var result = new FileOperationResult();

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await Task.Run(() =>
                    {
                        if (File.Exists(path))
                            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else if (Directory.Exists(path))
                            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }, ct);
                    result.FilesProcessed++;
                }
                catch (Exception ex) { result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public async Task<FileOperationResult> RenameFileAsync(string filePath, string newName, CancellationToken ct = default)
        {
            var result = new FileOperationResult();
            try
            {
                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    result.Errors.Add("Source not found");
                    return result;
                }

                var dir = Path.GetDirectoryName(filePath) ?? "";
                var newPath = Path.Combine(dir, newName);

                await Task.Run(() =>
                {
                    if (File.Exists(filePath)) File.Move(filePath, newPath);
                    else if (Directory.Exists(filePath)) Directory.Move(filePath, newPath);
                }, ct);

                result.FilesProcessed = 1;
                result.Success = true;
            }
            catch (Exception ex) { result.Errors.Add(ex.Message); }
            return result;
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int counter = 1;
            while (File.Exists(Path.Combine(dir, $"{name} ({counter}){ext}")))
                counter++;
            return Path.Combine(dir, $"{name} ({counter}){ext}");
        }

        private static string GetUniqueDirectoryPath(string path)
        {
            if (!Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileName(path);
            int counter = 1;
            while (Directory.Exists(Path.Combine(dir, $"{name} ({counter})")))
                counter++;
            return Path.Combine(dir, $"{name} ({counter})");
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), false);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)), ct);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
