using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfApp1.Models;

namespace WpfApp1.Services
{
    public sealed class MetadataCacheService : IDisposable
    {
        private readonly string _cacheDirectory;
        private bool _disposed;

        public MetadataCacheService()
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WpfApp1", "metadata-cache");
            Directory.CreateDirectory(_cacheDirectory);
        }

        private string CacheKeyFor(string filePath)
        {
            var fi = new FileInfo(filePath);
            var ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
            var raw = $"{filePath}|{ticks}";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)))[..16];
        }

        private string CachePathFor(string filePath) =>
            Path.Combine(_cacheDirectory, CacheKeyFor(filePath) + ".json");

        public bool HasCache(string filePath)
        {
            return File.Exists(CachePathFor(filePath));
        }

        public PhotoMetadata? GetCached(string filePath)
        {
            try
            {
                var path = CachePathFor(filePath);
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var element = JsonSerializer.Deserialize<JsonElement>(json);
                return PhotoMetadata.FromExifToolJson(filePath, element);
            }
            catch { return null; }
        }

        public async Task SetCacheAsync(string filePath, PhotoMetadata metadata, CancellationToken ct = default)
        {
            try
            {
                if (metadata.RawJson == null) return;
                await File.WriteAllTextAsync(CachePathFor(filePath), metadata.RawJson, ct);
            }
            catch { }
        }

        public void Invalidate(string filePath)
        {
            try { File.Delete(CachePathFor(filePath)); } catch { }
        }

        public void ClearAll()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                    File.Delete(file);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
