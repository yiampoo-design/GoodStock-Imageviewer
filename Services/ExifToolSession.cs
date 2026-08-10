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
            var argumentFile = Path.Combine(Path.GetTempPath(), $"gs-exif-{Guid.NewGuid():N}.args");
            try
            {
                var argsList = new List<string> { "-charset", "filename=UTF8" };
                argsList.AddRange(arguments);

                await File.WriteAllLinesAsync(argumentFile, argsList,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _binaryPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.StartInfo.ArgumentList.Add("-charset");
                process.StartInfo.ArgumentList.Add("filename=UTF8");
                process.StartInfo.ArgumentList.Add("-@");
                process.StartInfo.ArgumentList.Add(argumentFile);
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);

                var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(DefaultTimeoutMs);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(effectiveTimeout);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                    return new ExifToolResult(-1, await SafeAwait(stdoutTask), await SafeAwait(stderrTask), true);
                }

                return new ExifToolResult(process.ExitCode, await stdoutTask, await stderrTask, false);
            }
            finally
            {
                try { File.Delete(argumentFile); } catch { }
            }
        }

        private static async Task<string> SafeAwait(Task<string> task)
        {
            try { return await task; }
            catch { return ""; }
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
