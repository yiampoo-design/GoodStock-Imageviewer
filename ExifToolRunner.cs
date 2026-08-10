using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1
{
    public sealed record ExifToolResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut);

    public static class ExifToolRunner
    {
        public static async Task<ExifToolResult> RunAsync(
            string binaryPath,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var argumentFile = Path.Combine(
                Path.GetTempPath(), $"wpfapp1-exiftool-{Guid.NewGuid():N}.args");
            try
            {
                var argsList = arguments.ToList();
                await File.WriteAllLinesAsync(
                    argumentFile, argsList,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = binaryPath,
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

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                    return new(-1, await SafeAwait(stdoutTask), await SafeAwait(stderrTask), true);
                }

                return new(
                    process.ExitCode,
                    await stdoutTask,
                    await stderrTask,
                    TimedOut: false);
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
    }
}
