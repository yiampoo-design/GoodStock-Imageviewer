using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1.Services
{
    public sealed class ExifToolSession : IDisposable
    {
        private Process? _process;
        private readonly string _binaryPath;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _commandSemaphore = new(1, 1);
        private ConcurrentQueue<string> _stderrQueue = new();
        private CancellationTokenSource? _pumpCts;
        private bool _disposed;
        private int _requestCounter;
        private const int DefaultTimeoutMs = 30000;
        private const int ShutdownTimeoutMs = 5000;
        private const int RestartDelayMs = 500;
        private const int StderrSettleMs = 120;

        public ExifToolSession(string binaryPath)
        {
            _binaryPath = binaryPath;
        }

        public bool IsRunning
        {
            get
            {
                lock (_lock) return _process != null && !_process.HasExited;
            }
        }

        public void EnsureRunning()
        {
            lock (_lock)
            {
                if (_process != null && !_process.HasExited) return;
                StartProcessUnlocked();
            }
        }

        private void StartProcessUnlocked()
        {
            _pumpCts?.Cancel();
            _pumpCts?.Dispose();
            _stderrQueue = new ConcurrentQueue<string>();

            _process?.Dispose();
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _binaryPath,
                    Arguments = "-stay_open True -@ -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            _process.Start();
            AppLog.Info($"ExifTool session started ({_binaryPath})");

            _pumpCts = new CancellationTokenSource();
            _ = PumpStderrAsync(_pumpCts.Token);
        }

        private async Task PumpStderrAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(_process?.StandardError, ct);
                    if (line == null) break;
                    _stderrQueue.Enqueue(line);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void RestartProcess()
        {
            AppLog.Warn("ExifTool session crashed, restarting");
            lock (_lock)
            {
                _pumpCts?.Cancel();
                try { _process?.Kill(); } catch { }
                _process?.Dispose();
                _process = null;
                Thread.Sleep(RestartDelayMs);
                StartProcessUnlocked();
            }
            AppLog.Info("ExifTool session restarted");
        }

        public async Task<ExifToolResult> RunCommandAsync(
            IEnumerable<string> arguments,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            await _commandSemaphore.WaitAsync(ct);
            try
            {
                return await RunCommandInternalAsync(arguments, timeout, ct);
            }
            finally
            {
                _commandSemaphore.Release();
            }
        }

        private async Task<ExifToolResult> RunCommandInternalAsync(
            IEnumerable<string> arguments,
            TimeSpan? timeout,
            CancellationToken ct)
        {
            EnsureRunning();
            var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(DefaultTimeoutMs);

            lock (_lock)
            {
                if (_process == null || _process.HasExited)
                    return new ExifToolResult(-1, "", "ExifTool session not running", false);
            }

            while (_stderrQueue.TryDequeue(out _)) { }

            var requestId = Interlocked.Increment(ref _requestCounter);
            var readyMarker = $"{{ready{requestId}}}";

            var cmdBuilder = new StringBuilder();
            cmdBuilder.AppendLine("-charset");
            cmdBuilder.AppendLine("filename=UTF8");
            foreach (var arg in arguments)
                cmdBuilder.AppendLine(arg);
            cmdBuilder.AppendLine($"-execute{requestId}");

            var cmdText = cmdBuilder.ToString();

            lock (_lock)
            {
                if (_process == null || _process.HasExited)
                    return new ExifToolResult(-1, "", "ExifTool session died", false);

                try
                {
                    _process.StandardInput.Write(cmdText);
                    _process.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    RestartProcess();
                    return new ExifToolResult(-1, "", $"Write failed, session restarted: {ex.Message}", false);
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            var stdout = await ReadStdoutAsync(readyMarker, timeoutCts.Token);
            var stderr = await DrainStderrAsync(timeoutCts.Token);

            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                AppLog.Warn("ExifTool command timed out");
                RestartProcess();
                return new ExifToolResult(-1, "", "Command timed out, session restarted", true);
            }

            if (stdout.Length == 0 && stderr.Length == 0)
            {
                RestartProcess();
                return new ExifToolResult(-1, "", "ExifTool produced no output, session restarted", false);
            }

            return new ExifToolResult(0, stdout.TrimEnd(), stderr.TrimEnd(), false);
        }

        private async Task<string> ReadStdoutAsync(string readyMarker, CancellationToken ct)
        {
            var sb = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(_process?.StandardOutput, ct);
                    if (line == null) break;
                    if (line == readyMarker) break;
                    sb.AppendLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            return sb.ToString();
        }

        private async Task<string> DrainStderrAsync(CancellationToken ct)
        {
            var lines = new List<string>();
            try
            {
                var quiet = 0;
                while (!ct.IsCancellationRequested)
                {
                    bool drained = false;
                    while (_stderrQueue.TryDequeue(out var line))
                    {
                        lines.Add(line);
                        drained = true;
                    }
                    if (drained) { quiet = 0; }
                    else
                    {
                        if (++quiet >= 2) break;
                        await Task.Delay(StderrSettleMs, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            return string.Join(Environment.NewLine, lines);
        }

        private static async Task<string?> ReadLineAsync(System.IO.StreamReader? reader, CancellationToken ct)
        {
            if (reader == null) return null;
            try { return await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) { return null; }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _commandSemaphore.Dispose();
            lock (_lock)
            {
                _pumpCts?.Cancel();
                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.StandardInput.Write("-stay_open\nFalse\n");
                        _process.StandardInput.Flush();
                        _process.WaitForExit(ShutdownTimeoutMs);
                    }
                    catch
                    {
                        try { _process.Kill(true); } catch { }
                    }
                }
                _process?.Dispose();
                _process = null;
            }
        }
    }
}
