using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using WPCL.Models;

namespace WPCL.Services;

public sealed class DownloadService
{
    private readonly HttpClient _client;

    public DownloadService(HttpClient client)
    {
        _client = client;
    }

    public async Task DownloadManyAsync(
        IReadOnlyList<DownloadItem> items,
        int concurrency,
        string operation,
        string stage,
        Action<ProgressUpdate> report,
        CancellationToken cancellationToken)
    {
        var downloadItems = items
            .GroupBy(item => Path.GetFullPath(item.Destination), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.Sha1))
                .First())
            .ToArray();
        var completed = 0;
        long downloaded = 0;
        var totalBytes = downloadItems.Sum(item => Math.Max(0, item.Size));
        var stopwatch = Stopwatch.StartNew();
        long lastReport = 0;

        void Report(string? current, bool force = false)
        {
            var elapsed = stopwatch.ElapsedMilliseconds;
            var previous = Interlocked.Read(ref lastReport);
            if (!force && elapsed - previous < 120)
            {
                return;
            }
            Interlocked.Exchange(ref lastReport, elapsed);
            report(new ProgressUpdate(
                operation,
                stage,
                Volatile.Read(ref completed),
                downloadItems.Length,
                Interlocked.Read(ref downloaded),
                totalBytes,
                current));
        }

        await Parallel.ForEachAsync(
            downloadItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 32),
                CancellationToken = cancellationToken
            },
            async (item, token) =>
            {
                if (await IsValidAsync(item.Destination, item.Sha1, token))
                {
                    Interlocked.Add(ref downloaded, Math.Max(0, item.Size));
                }
                else
                {
                    await DownloadWithRetryAsync(item, bytes =>
                    {
                        Interlocked.Add(ref downloaded, bytes);
                        Report(item.Label);
                    }, token);
                }

                Interlocked.Increment(ref completed);
                Report(item.Label, true);
            });

        Report(null, true);
    }

    private async Task DownloadWithRetryAsync(DownloadItem item, Action<int> addBytes, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var temporary = $"{item.Destination}.{Environment.ProcessId}.{Guid.NewGuid():N}.download";
            long attemptBytes = 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                using var response = await _client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        attemptBytes += read;
                        addBytes(read);
                    }
                    await output.FlushAsync(cancellationToken);
                }

                if (!await IsValidAsync(temporary, item.Sha1, cancellationToken))
                {
                    throw new InvalidDataException($"{item.Label} 的 SHA-1 校验失败。");
                }

                await CommitAsync(temporary, item, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                addBytes((int)-Math.Min(int.MaxValue, attemptBytes));
                TryDelete(temporary);
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
                }
            }
        }

        throw new IOException($"下载 {item.Label} 失败：{lastError?.Message}", lastError);
    }

    private static async Task<bool> IsValidAsync(string path, string expectedSha1, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            return true;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hash = await SHA1.HashDataAsync(stream, cancellationToken);
            return string.Equals(Convert.ToHexString(hash), expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task CommitAsync(
        string temporary,
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (await IsValidAsync(item.Destination, item.Sha1, cancellationToken))
            {
                TryDelete(temporary);
                return;
            }

            try
            {
                File.Move(temporary, item.Destination, true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
            }

            if (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        throw new IOException($"无法写入 {item.Destination}：{lastError?.Message}", lastError);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
