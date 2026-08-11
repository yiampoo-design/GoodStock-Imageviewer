using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1.Services
{
    public sealed class ExifToolRuntimeService : IDisposable
    {
        private readonly string _appToolsDir;
        private readonly string _writableToolsDir;
        private string? _binaryPath;
        private ExifToolSession? _session;
        private bool _disposed;

        public event EventHandler? RuntimeAvailable;

        public string? BinaryPath => _binaryPath;
        public bool IsAvailable => _binaryPath != null;

        public ExifToolRuntimeService()
        {
            _appToolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            _writableToolsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WpfApp1", "tools");
        }

        public void ProbeExisting()
        {
            if (_disposed) return;
            var inspection = ExifToolService.Inspect(_appToolsDir, _writableToolsDir);
            if (inspection.Valid)
            {
                var oldPath = _binaryPath;
                _binaryPath = inspection.BinaryPath;
                if (oldPath == null && _binaryPath != null)
                    RuntimeAvailable?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<bool> InstallAsync(
            Func<long, long?, Task>? progress = null,
            CancellationToken ct = default)
        {
            if (_disposed) return false;

            Directory.CreateDirectory(_writableToolsDir);
            var archivePath = Path.Combine(_writableToolsDir, "exiftool.zip");
            await ExifToolService.DownloadArchiveAsync(archivePath, progress, ct);
            var binaryPath = await ExifToolService.InstallArchiveAtomicallyAsync(
                archivePath, _writableToolsDir, ct);

            _binaryPath = binaryPath;
            RuntimeAvailable?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public ExifToolSession GetSession()
        {
            if (_binaryPath == null)
                throw new InvalidOperationException("ExifTool runtime is not available.");

            if (_session == null || _session.IsRunning == false)
            {
                _session?.Dispose();
                _session = new ExifToolSession(_binaryPath);
            }
            return _session;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
