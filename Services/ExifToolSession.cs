using System;
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
        private bool _disposed;
        private const int DefaultTimeoutMs = 30000;

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
                StartProcess();
            }
        }

        private void StartProcess()
        {
            _process?.Dispose();
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _binaryPath,
                    Arguments = "-stay_open True -overwrite_original",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            _process.Start();
        }

        public async Task<ExifToolResult> RunCommandAsync(
            IEnumerable<string> arguments,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            EnsureRunning();
            var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(DefaultTimeoutMs);

            lock (_lock)
            {
                if (_process == null || _process.HasExited)
                    return new ExifToolResult(-1, "", "ExifTool session not running", false);
            }

            var cmdBuilder = new StringBuilder();
            cmdBuilder.AppendLine("-charset");
            cmdBuilder.AppendLine("filename=UTF8");
            foreach (var arg in arguments)
                cmdBuilder.AppendLine(arg);
            cmdBuilder.AppendLine("-execute");
            cmdBuilder.AppendLine("-stay_open");
            cmdBuilder.AppendLine("True");

            var cmdText = cmdBuilder.ToString();

            string stdout;
            string stderr;

            lock (_lock)
            {
                if (_process == null || _process.HasExited)
                    return new ExifToolResult(-1, "", "ExifTool session died", false);

                _process.StandardInput.Write(cmdText);
                _process.StandardInput.Flush();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            var stdoutTask = Task.Run(async () =>
            {
                var sb = new StringBuilder();
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(_process?.StandardOutput);
                    if (line == null) break;
                    if (line == "{ready}") break;
                    sb.AppendLine(line);
                }
                return sb.ToString();
            }, timeoutCts.Token);

            var stderrTask = Task.Run(async () =>
            {
                var sb = new StringBuilder();
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(_process?.StandardError);
                    if (line == null) break;
                    if (line == "{ready}") break;
                    sb.AppendLine(line);
                }
                return sb.ToString();
            }, timeoutCts.Token);

            try
            {
                stdout = await stdoutTask;
                stderr = await stderrTask;
                return new ExifToolResult(0, stdout.TrimEnd(), stderr.TrimEnd(), false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new ExifToolResult(-1, "", "Command timed out", true);
            }
        }

        private static async Task<string?> ReadLineAsync(System.IO.StreamReader? reader)
        {
            if (reader == null) return null;
            try { return await reader.ReadLineAsync(); }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.StandardInput.Write("-stay_open\nFalse\n");
                        _process.StandardInput.Flush();
                        _process.WaitForExit(5000);
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
