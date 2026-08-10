using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1
{
    public sealed record ExifToolInspection(
        bool Exists,
        bool Valid,
        string? BinaryPath,
        string Message);

    public static class ExifToolService
    {
        public const string ApprovedVersion = "13.59";
        public const string ApprovedAsset = "exiftool-13.59_64.zip";
        public const string ApprovedUrl =
            "https://sourceforge.net/projects/exiftool/files/exiftool-13.59_64.zip/download";
        public const string ApprovedSha256 =
            "44B512B25AF500724BA579D0A53C8FC5851628B692DD5E5D94AE4A15C2CBA9EC";
        public const string RuntimeDirectoryName = "exiftool-13.59";
        public const string ManifestFileName = "exiftool-runtime.json";

        private const int MaxRedirects = 8;
        private static readonly Uri ApprovedDownloadUri = new(ApprovedUrl);
        private static readonly string ApprovedMirrorPath = $"/project/exiftool/{ApprovedAsset}";

        public static string ManagedRuntimeDirectory(string toolsDirectory) =>
            Path.Combine(toolsDirectory, RuntimeDirectoryName);

        public static ExifToolInspection Inspect(
            string applicationToolsDirectory,
            string writableToolsDirectory)
        {
            ExifToolInspection? firstInvalid = null;
            foreach (var candidate in new[]
            {
                ManagedRuntimeDirectory(writableToolsDirectory),
                ManagedRuntimeDirectory(applicationToolsDirectory),
            })
            {
                var inspection = InspectDirectory(candidate);
                if (!inspection.Exists) continue;
                if (inspection.Valid) return inspection;
                firstInvalid ??= inspection;
            }

            return firstInvalid ?? new(false, false, null,
                "ExifTool is not installed. Click Download to install.");
        }

        public static ExifToolInspection InspectDirectory(string directory)
        {
            var binaryPath = Path.Combine(directory, "exiftool.exe");
            if (!File.Exists(binaryPath))
                return new(false, false, null, "exiftool.exe is missing.");

            RuntimeManifest? manifest;
            try
            {
                var manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath))
                    return new(true, false, binaryPath,
                        "ExifTool runtime manifest is missing; re-download ExifTool.");
                manifest = JsonSerializer.Deserialize<RuntimeManifest>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return new(true, false, binaryPath,
                    "ExifTool runtime manifest is unreadable; re-download ExifTool.");
            }

            if (manifest is null
                || !string.Equals(manifest.Version, ApprovedVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.Asset, ApprovedAsset, StringComparison.Ordinal)
                || !string.Equals(manifest.Sha256, ApprovedSha256, StringComparison.OrdinalIgnoreCase)
                || manifest.Files is not { Length: > 0 })
            {
                return new(true, false, binaryPath,
                    "ExifTool runtime is not the approved version; re-download ExifTool.");
            }

            foreach (var file in manifest.Files)
            {
                string fullPath;
                try { fullPath = ResolveChildPath(directory, file.Name); }
                catch { return new(true, false, binaryPath, "ExifTool runtime manifest contains an unsafe path."); }
                if (!File.Exists(fullPath) || new FileInfo(fullPath).Length != file.Length || file.Length <= 0)
                    return new(true, false, binaryPath,
                        "ExifTool runtime is incomplete; re-download ExifTool.");
            }

            if (!Directory.Exists(Path.Combine(directory, "exiftool_files")))
                return new(true, false, binaryPath,
                    "ExifTool support files are missing; re-download ExifTool.");

            return new(true, true, binaryPath, $"ExifTool {ApprovedVersion} is ready.");
        }

        public static bool IsAllowedDownloadUri(Uri uri, bool initialRequest)
        {
            if (!uri.IsAbsoluteUri
                || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !uri.IsDefaultPort
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
                return false;

            if (initialRequest)
                return Uri.Compare(uri, ApprovedDownloadUri,
                    UriComponents.AbsoluteUri, UriFormat.SafeUnescaped,
                    StringComparison.Ordinal) == 0;

            var host = uri.IdnHost;
            var trustedHost = host.Equals("sourceforge.net", StringComparison.OrdinalIgnoreCase)
                || host.Equals("downloads.sourceforge.net", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".dl.sourceforge.net", StringComparison.OrdinalIgnoreCase);
            return trustedHost
                && uri.AbsolutePath.Equals(ApprovedMirrorPath, StringComparison.Ordinal);
        }

        public static async Task DownloadArchiveAsync(
            string destinationPath,
            Func<long, long?, Task>? reportProgress,
            CancellationToken cancellationToken)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var current = ApprovedDownloadUri;

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!IsAllowedDownloadUri(current, initialRequest: hop == 0))
                    throw new HttpRequestException("ExifTool download redirected to an untrusted URL.");

                using var response = await http.GetAsync(
                    current.ToString(), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location
                        ?? throw new HttpRequestException("ExifTool returned a redirect without a destination.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException(
                        $"ExifTool download failed with HTTP {(int)response.StatusCode}.");

                var expectedLength = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    1024 * 1024, useAsync: true);
                var buffer = new byte[1024 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (reportProgress is not null)
                        await reportProgress(received, expectedLength);
                }
                return;
            }

            throw new HttpRequestException("ExifTool download exceeded the redirect limit.");
        }

        public static async Task<string> InstallArchiveAtomicallyAsync(
            string archivePath,
            string writableToolsDirectory,
            CancellationToken cancellationToken)
        {
            await VerifyArchiveHashAsync(archivePath, cancellationToken);

            Directory.CreateDirectory(writableToolsDirectory);
            var finalDirectory = ManagedRuntimeDirectory(writableToolsDirectory);
            var stageDirectory = Path.Combine(writableToolsDirectory,
                $".{RuntimeDirectoryName}.stage-{Guid.NewGuid():N}");
            var backupDirectory = Path.Combine(writableToolsDirectory,
                $".{RuntimeDirectoryName}.backup-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(stageDirectory);
                await ExtractApprovedFilesAsync(archivePath, stageDirectory, cancellationToken);

                var files = Directory.EnumerateFiles(stageDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => new RuntimeManifestFile(
                        Path.GetRelativePath(stageDirectory, path).Replace('\\', '/'),
                        new FileInfo(path).Length))
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var manifest = new RuntimeManifest(
                    ApprovedVersion, ApprovedAsset, ApprovedSha256, files);
                await File.WriteAllTextAsync(
                    Path.Combine(stageDirectory, ManifestFileName),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);

                var inspection = InspectDirectory(stageDirectory);
                if (!inspection.Valid) throw new InvalidDataException(inspection.Message);

                if (Directory.Exists(finalDirectory)) Directory.Move(finalDirectory, backupDirectory);
                try { Directory.Move(stageDirectory, finalDirectory); }
                catch
                {
                    if (Directory.Exists(backupDirectory) && !Directory.Exists(finalDirectory))
                        Directory.Move(backupDirectory, finalDirectory);
                    throw;
                }
                TryDeleteDirectory(backupDirectory);
                TryDeleteLegacyRuntime(writableToolsDirectory);
                return Path.Combine(finalDirectory, "exiftool.exe");
            }
            finally
            {
                TryDeleteDirectory(stageDirectory);
                if (Directory.Exists(backupDirectory) && !Directory.Exists(finalDirectory))
                    Directory.Move(backupDirectory, finalDirectory);
                else
                    TryDeleteDirectory(backupDirectory);
            }
        }

        private static async Task VerifyArchiveHashAsync(
            string archivePath,
            CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(archivePath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(ApprovedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"ExifTool archive hash mismatch. Expected {ApprovedSha256}, got {actual}.");
        }

        private static async Task ExtractApprovedFilesAsync(
            string archivePath,
            string stageDirectory,
            CancellationToken cancellationToken)
        {
            var executableFound = false;
            var supportFileFound = false;
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                var normalized = entry.FullName.Replace('\\', '/');
                string? relative = null;
                if (entry.Name.Equals("exiftool(-k).exe", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("exiftool.exe", StringComparison.OrdinalIgnoreCase))
                {
                    relative = "exiftool.exe";
                    executableFound = true;
                }
                else
                {
                    var marker = "exiftool_files/";
                    var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        relative = normalized[index..];
                        supportFileFound = true;
                    }
                }

                if (relative is null) continue;
                var destination = ResolveChildPath(stageDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 1024, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!executableFound || !supportFileFound)
                throw new InvalidDataException("ExifTool archive is missing its executable or support files.");
        }

        private static string ResolveChildPath(string root, string relativePath)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath));
            if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ExifTool archive contains an unsafe path.");
            return candidate;
        }

        private static void TryDeleteLegacyRuntime(string writableToolsDirectory)
        {
            try { File.Delete(Path.Combine(writableToolsDirectory, "exiftool.exe")); } catch { }
            TryDeleteDirectory(Path.Combine(writableToolsDirectory, "exiftool_files"));
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { }
        }

        private sealed record RuntimeManifest(
            string Version,
            string Asset,
            string Sha256,
            RuntimeManifestFile[] Files);

        private sealed record RuntimeManifestFile(string Name, long Length);
    }
}
