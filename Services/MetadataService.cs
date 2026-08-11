using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public sealed class MetadataService : IMetadataService
    {
        private readonly ExifToolRuntimeService _runtime;
        private readonly MetadataCacheService _cacheService;
        private bool _disposed;

        public string? ExifToolPath => _runtime.BinaryPath;

        public event EventHandler? AvailabilityChanged;

        public void InvalidateCache(string filePath)
        {
            _cacheService.Invalidate(filePath);
        }

        public MetadataService(ExifToolRuntimeService runtime)
        {
            _runtime = runtime;
            _cacheService = new MetadataCacheService();
            _runtime.RuntimeAvailable += OnRuntimeAvailable;
        }

        private void OnRuntimeAvailable(object? sender, EventArgs e)
        {
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<bool> IsAvailableAsync()
        {
            return Task.FromResult(_runtime.IsAvailable);
        }

        public async Task<PhotoMetadata?> ReadMetadataAsync(string filePath, CancellationToken ct = default)
        {
            if (!_runtime.IsAvailable || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var cached = _cacheService.GetCached(filePath);
                if (cached != null) return cached;

                var session = _runtime.GetSession();
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
                    "-FocalLength", "-FNumber", "-ExposureTime", "-ISO",
                    "-Orientation", "-ImageWidth", "-ImageHeight",
                    "-FileSize", "-FileType", "-Rating",
                    "-GPSLatitude#", "-GPSLongitude#",
                    "-GPSLatitudeRef", "-GPSLongitudeRef",
                    "-Composite:GPSLatitude#", "-Composite:GPSLongitude#",
                    "-ColorSpace", "-ICC_Profile:ProfileDescription",
                    "--", filePath
                };

                var result = await session.RunCommandAsync(args, TimeSpan.FromSeconds(15), ct);
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
            if (!_runtime.IsAvailable)
                return new MetadataWriteResult { Success = false, ErrorMessage = "ExifTool is not installed." };
            if (!patch.HasChanges)
                return new MetadataWriteResult { Success = false, ErrorMessage = "No changes to apply." };
            if (!File.Exists(filePath))
                return new MetadataWriteResult { Success = false, ErrorMessage = "File not found." };

            var backupPath = filePath + ".gsbak";
            try
            {
                File.Copy(filePath, backupPath, true);

                var session = _runtime.GetSession();
                var args = new List<string> { "-overwrite_original", "-sep", ", " };
                args.AddRange(patch.ToExifToolArgs());
                args.Add("--");
                args.Add(filePath);

                var result = await session.RunCommandAsync(args, TimeSpan.FromSeconds(30), ct);
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

                var verified_meta = await ReadMetadataAsync(filePath, ct);
                var mismatches = new List<string>();
                if (verified_meta != null)
                {
                    if (patch.Title != null && verified_meta.Title != patch.Title)
                        mismatches.Add($"Title: expected '{patch.Title}', got '{verified_meta.Title}'");
                    if (patch.Description != null && verified_meta.Description != patch.Description)
                        mismatches.Add("Description mismatch");
                    if (patch.Creator != null && verified_meta.Creator != patch.Creator)
                        mismatches.Add($"Creator: expected '{patch.Creator}', got '{verified_meta.Creator}'");
                    if (patch.Copyright != null && verified_meta.Copyright != patch.Copyright)
                        mismatches.Add($"Copyright: expected '{patch.Copyright}', got '{verified_meta.Copyright}'");
                    if (patch.Keywords != null)
                    {
                        var expected = string.Join(", ", patch.Keywords.OrderBy(k => k));
                        var actual = verified_meta.Keywords.Count > 0 ? string.Join(", ", verified_meta.Keywords.OrderBy(k => k)) : "";
                        if (expected != actual)
                            mismatches.Add($"Keywords: expected '{expected}', got '{actual}'");
                    }
                    if (patch.DateTaken != null)
                    {
                        if (verified_meta.DateTaken == null)
                        {
                            mismatches.Add("DateTaken: expected value but got null");
                        }
                        else if (DateTime.TryParse(patch.DateTaken, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expectedDate))
                        {
                            var diff = Math.Abs((verified_meta.DateTaken.Value - expectedDate).TotalSeconds);
                            if (diff > 2)
                                mismatches.Add($"DateTaken: expected '{patch.DateTaken}', got '{verified_meta.DateTaken}'");
                        }
                        else
                        {
                            mismatches.Add($"DateTaken: user input '{patch.DateTaken}' is not a valid date");
                        }
                    }
                }

                bool verificationPassed = verified_meta != null && mismatches.Count == 0;
                if (!verificationPassed)
                {
                    AppLog.Warn($"Metadata verification failed for {Path.GetFileName(filePath)}: {string.Join("; ", mismatches)}");
                    try { File.Copy(backupPath, filePath, true); } catch { }
                }

                return new MetadataWriteResult
                {
                    Success = verificationPassed,
                    VerifiedMetadata = verified_meta,
                    VerificationSucceeded = verificationPassed,
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
            _runtime.RuntimeAvailable -= OnRuntimeAvailable;
            _cacheService.Dispose();
        }
    }
}
