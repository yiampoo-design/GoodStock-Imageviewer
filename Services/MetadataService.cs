using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public sealed class MetadataService : IMetadataService
    {
        private readonly string? _exifToolPath;
        private readonly MetadataCacheService _cacheService;
        private bool _disposed;

        public string? ExifToolPath => _exifToolPath;

        public void InvalidateCache(string filePath)
        {
            _cacheService.Invalidate(filePath);
        }

        public MetadataService()
        {
            var appTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            var writableTools = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WpfApp1", "tools");
            var inspection = ExifToolService.Inspect(appTools, writableTools);
            _exifToolPath = inspection.Valid ? inspection.BinaryPath : null;
            _cacheService = new MetadataCacheService();
        }

        public async Task<bool> IsAvailableAsync()
        {
            if (_exifToolPath == null) return false;
            try
            {
                var result = await ExifToolRunner.RunAsync(_exifToolPath,
                    new[] { "-ver" }, TimeSpan.FromSeconds(5));
                return result.ExitCode == 0;
            }
            catch { return false; }
        }

        public async Task<PhotoMetadata?> ReadMetadataAsync(string filePath, CancellationToken ct = default)
        {
            if (_exifToolPath == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var cached = _cacheService.GetCached(filePath);
                if (cached != null) return cached;

                var args = new[]
                {
                    "-json", "-G1",
                    "-XMP-dc:Title", "-XMP-dc:Description", "-XMP-dc:Subject", "-XMP-dc:Creator",
                    "-XMP-dc:Rights",
                    "-IPTC:ObjectName", "-IPTC:Caption-Abstract", "-IPTC:Keywords", "-IPTC:By-line",
                    "-IPTC:CopyrightNotice",
                    "-ImageDescription", "-Artist", "-Copyright",
                    "-DateTimeOriginal", "-DateTime",
                    "-Make", "-Model", "-LensModel",
                    "-Orientation", "-ImageWidth", "-ImageHeight",
                    "-FileSize", "-FileType", "-Rating",
                    "-GPSLatitude#", "-GPSLongitude#",
                    "-ColorSpace", "-ICC_Profile:ProfileDescription",
                    "--", filePath
                };

                var result = await ExifToolRunner.RunAsync(_exifToolPath, args, TimeSpan.FromSeconds(15), ct);
                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                    return null;

                var arr = JsonSerializer.Deserialize<JsonElement>(result.StandardOutput);
                if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                {
                    var meta = PhotoMetadata.FromExifToolJson(filePath, arr[0]);
                    _ = _cacheService.SetCacheAsync(filePath, meta, ct);
                    return meta;
                }
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        public async Task<MetadataWriteResult> WriteMetadataAsync(string filePath, MetadataPatch patch, CancellationToken ct = default)
        {
            if (_exifToolPath == null)
                return new MetadataWriteResult { Success = false, ErrorMessage = "ExifTool is not installed." };
            if (!patch.HasChanges)
                return new MetadataWriteResult { Success = false, ErrorMessage = "No changes to apply." };
            if (!File.Exists(filePath))
                return new MetadataWriteResult { Success = false, ErrorMessage = "File not found." };

            var backupPath = filePath + ".gsbak";
            try
            {
                File.Copy(filePath, backupPath, true);

                var args = new List<string> { "-overwrite_original", "-sep", ", " };
                args.AddRange(patch.ToExifToolArgs());
                args.Add("--");
                args.Add(filePath);

                var result = await ExifToolRunner.RunAsync(_exifToolPath, args, TimeSpan.FromSeconds(30), ct);
                if (result.ExitCode != 0)
                {
                    try { File.Copy(backupPath, filePath, true); } catch { }
                    return new MetadataWriteResult
                    {
                        Success = false,
                        ErrorMessage = result.StandardError.Length > 500
                            ? result.StandardError[..500] : result.StandardError
                    };
                }

                _cacheService.Invalidate(filePath);

                var verified = await ReadMetadataAsync(filePath, ct);
                var mismatches = new List<string>();
                if (verified != null)
                {
                    if (patch.Title != null && verified.Title != patch.Title)
                        mismatches.Add($"Title: expected '{patch.Title}', got '{verified.Title}'");
                    if (patch.Description != null && verified.Description != patch.Description)
                        mismatches.Add("Description mismatch");
                    if (patch.Creator != null && verified.Creator != patch.Creator)
                        mismatches.Add($"Creator: expected '{patch.Creator}', got '{verified.Creator}'");
                    if (patch.Copyright != null && verified.Copyright != patch.Copyright)
                        mismatches.Add($"Copyright: expected '{patch.Copyright}', got '{verified.Copyright}'");
                }

                return new MetadataWriteResult
                {
                    Success = true,
                    VerifiedMetadata = verified,
                    VerificationSucceeded = verified != null && mismatches.Count == 0,
                    MismatchedFields = mismatches
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                try { if (File.Exists(backupPath)) File.Copy(backupPath, filePath, true); } catch { }
                return new MetadataWriteResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                try { File.Delete(backupPath); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cacheService.Dispose();
        }
    }
}
