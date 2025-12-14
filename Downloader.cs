using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher
{
    public class Downloader
    {
        private HttpClient _httpClient;

        public Downloader()
        {
            // Configure TLS settings globally - force TLS 1.2 for Windows 11 compatibility
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.CheckCertificateRevocationList = false;
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
            
            _httpClient = CreateHttpClient();
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.Zero,
                PooledConnectionIdleTimeout = TimeSpan.Zero,
                SslOptions = new SslClientAuthenticationOptions
                {
                    // Force TLS 1.2 only for maximum compatibility with Windows 11
                    EnabledSslProtocols = SslProtocols.Tls12,
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                    AllowRenegotiation = true,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                ConnectTimeout = TimeSpan.FromSeconds(60),
                MaxConnectionsPerServer = 4,
                ResponseDrainTimeout = TimeSpan.FromSeconds(5)
            };

            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Connection.Clear();
            client.DefaultRequestHeaders.Connection.Add("keep-alive");
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            client.DefaultRequestHeaders.ExpectContinue = false;
            
            return client;
        }

        private void ResetHttpClient()
        {
            var oldClient = _httpClient;
            _httpClient = CreateHttpClient();
            try { oldClient.Dispose(); } catch { }
            Logger.Log("HttpClient reset");
        }

        public async Task<string?> DownloadStringAsync(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                return await _httpClient.GetStringAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error downloading string from {url}: {ex.Message}");
                return null;
            }
        }

        public async Task<byte[]?> DownloadBytesAsync(string url, IProgress<int>? progress = null, int baseProgress = 0, int maxProgress = 100)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                long? totalSize = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var memoryStream = new MemoryStream();

                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                int progressUpdateCounter = 0;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    downloaded += bytesRead;
                    progressUpdateCounter++;

                    if (progressUpdateCounter >= 50 || downloaded == totalSize)
                    {
                        int currentProgress = totalSize.HasValue && totalSize.Value > 0
                            ? baseProgress + (int)((downloaded * (maxProgress - baseProgress)) / totalSize.Value)
                            : baseProgress + Math.Min(maxProgress - baseProgress, (int)(downloaded / 1024));
                        progress?.Report(currentProgress);
                        progressUpdateCounter = 0;
                    }
                }

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error downloading bytes from {url}: {ex.Message}");
                return null;
            }
        }

        public async Task<long?> GetContentLengthAsync(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                return response.Content.Headers.ContentLength;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error getting content length from {url}: {ex.Message}");
                return null;
            }
        }

        public async Task DownloadFileAsync(string url, string filePath, IProgress<int>? progress = null, int baseProgress = 0, int maxProgress = 100)
        {
            const int maxRetries = 7;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Logger.Log($"Starting download (attempt {attempt}/{maxRetries}): {url}");

                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }

                    // Try BITS first (most reliable on Windows 11, handles TLS natively)
                    if (attempt <= 2)
                    {
                        await DownloadWithBitsAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    // Then try curl.exe with TLS 1.2
                    else if (attempt <= 4)
                    {
                        await DownloadWithCurlAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    // Then try chunked HttpClient download
                    else if (attempt <= 6)
                    {
                        await DownloadWithChunkedHttpClientAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    // Finally try standard HttpClient
                    else
                    {
                        await DownloadWithHttpClientAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    
                    RemoveZoneIdentifier(filePath);
                    Logger.Log($"Download completed: {filePath}");
                    return;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    LogException(ex, url, attempt, maxRetries);
                    
                    int delayMs = 2000 * attempt;
                    Logger.Log($"Retrying in {delayMs / 1000} seconds...");
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogException(ex, url, attempt, maxRetries);
                }
            }

            throw lastException ?? new Exception($"Failed to download {url} after {maxRetries} attempts");
        }

        /// <summary>
        /// Download using BITS (Background Intelligent Transfer Service) - most reliable on Windows 11
        /// </summary>
        private async Task DownloadWithBitsAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using BITS for download");
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string escapedUrl = url.Replace("'", "''");
            string escapedPath = filePath.Replace("'", "''");
            string jobName = $"LauncherDownload_{Guid.NewGuid():N}";
            
            // Use BITS with TLS 1.2 - BITS handles certificate validation properly on Windows 11
            string script = $@"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Force TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::CheckCertificateRevocationList = $false

try {{
    # Use Start-BitsTransfer with security policy set
    $job = Start-BitsTransfer -Source '{escapedUrl}' -Destination '{escapedPath}' -Priority Foreground -DisplayName '{jobName}' -Asynchronous
    
    while (($job.JobState -eq 'Transferring') -or ($job.JobState -eq 'Connecting')) {{
        Start-Sleep -Milliseconds 500
    }}
    
    if ($job.JobState -eq 'Transferred') {{
        Complete-BitsTransfer -BitsJob $job
    }} else {{
        $errorMsg = $job.ErrorDescription
        Remove-BitsTransfer -BitsJob $job -ErrorAction SilentlyContinue
        throw ""BITS transfer failed: $errorMsg""
    }}
}} catch {{
    throw $_.Exception.Message
}}
";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            
            var errorBuilder = new System.Text.StringBuilder();
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
            
            process.Start();
            process.BeginErrorReadLine();

            var progressTask = Task.Run(async () =>
            {
                int progressValue = baseProgress;
                while (!process.HasExited)
                {
                    await Task.Delay(500);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            progressValue = Math.Min(maxProgress - 5, progressValue + 2);
                            progress?.Report(progressValue);
                        }
                        catch { }
                    }
                }
            });

            bool exited = await Task.Run(() => process.WaitForExit(60 * 60 * 1000));
            
            if (!exited)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("BITS download timed out");
            }

            await progressTask;
            
            if (process.ExitCode != 0)
            {
                string error = errorBuilder.ToString();
                Logger.Log($"BITS error: {error}");
                throw new Exception($"BITS download failed: {error}");
            }

            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            {
                throw new Exception("BITS download failed - file not found or empty");
            }

            progress?.Report(maxProgress);
            Logger.Log($"BITS download completed. File size: {new FileInfo(filePath).Length} bytes");
        }

        /// <summary>
        /// Download using curl.exe with TLS 1.2 forced
        /// </summary>
        private async Task DownloadWithCurlAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using curl.exe for download with TLS 1.2");
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Force TLS 1.2, disable certificate revocation check
            var psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"--tlsv1.2 --tls-max 1.2 --location --fail --silent --show-error --ssl-no-revoke --output \"{filePath}\" --retry 3 --retry-delay 2 --connect-timeout 60 --max-time 3600 \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            
            var errorBuilder = new System.Text.StringBuilder();
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
            
            process.Start();
            process.BeginErrorReadLine();

            var progressTask = Task.Run(async () =>
            {
                int progressValue = baseProgress;
                while (!process.HasExited)
                {
                    await Task.Delay(1000);
                    
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            progressValue = Math.Min(maxProgress - 5, progressValue + 2);
                            progress?.Report(progressValue);
                        }
                        catch { }
                    }
                }
            });

            bool exited = await Task.Run(() => process.WaitForExit(60 * 60 * 1000));
            
            if (!exited)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("curl download timed out after 60 minutes");
            }

            await progressTask;
            
            string error = errorBuilder.ToString();
            
            if (process.ExitCode != 0)
            {
                Logger.Log($"curl error: {error}");
                throw new Exception($"curl download failed with exit code {process.ExitCode}: {error}");
            }

            if (!File.Exists(filePath))
            {
                throw new Exception("Download completed but file not found");
            }

            var finalFileInfo = new FileInfo(filePath);
            if (finalFileInfo.Length == 0)
            {
                throw new Exception("Downloaded file is empty");
            }

            progress?.Report(maxProgress);
            Logger.Log($"curl download completed. File size: {finalFileInfo.Length} bytes");
        }

        /// <summary>
        /// Download using HttpClient with chunked/range requests for better reliability
        /// </summary>
        private async Task DownloadWithChunkedHttpClientAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using chunked HttpClient for download");
            
            ResetHttpClient();
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // First, get the content length
            long? totalSize = await GetContentLengthAsync(url);
            
            if (!totalSize.HasValue || totalSize.Value <= 0)
            {
                // Fall back to regular download if we can't get content length
                Logger.Log("Cannot determine content length, falling back to regular download");
                await DownloadWithHttpClientAsync(url, filePath, progress, baseProgress, maxProgress);
                return;
            }

            Logger.Log($"Total file size: {totalSize.Value} bytes");

            const long chunkSize = 5 * 1024 * 1024; // 5MB chunks
            long downloaded = 0;
            int chunkNumber = 0;

            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            while (downloaded < totalSize.Value)
            {
                long rangeStart = downloaded;
                long rangeEnd = Math.Min(downloaded + chunkSize - 1, totalSize.Value - 1);
                
                Logger.Log($"Downloading chunk {++chunkNumber}: bytes {rangeStart}-{rangeEnd}");

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, rangeEnd);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };

                int chunkRetries = 3;
                bool chunkSuccess = false;

                for (int retry = 0; retry < chunkRetries && !chunkSuccess; retry++)
                {
                    try
                    {
                        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent && 
                            response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            throw new Exception($"Unexpected status code: {response.StatusCode}");
                        }

                        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                        byte[] buffer = new byte[32768];
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                            downloaded += bytesRead;

                            int currentProgress = baseProgress + (int)((downloaded * (maxProgress - baseProgress)) / totalSize.Value);
                            progress?.Report(currentProgress);
                        }

                        chunkSuccess = true;
                    }
                    catch (Exception ex) when (retry < chunkRetries - 1)
                    {
                        Logger.Log($"Chunk {chunkNumber} failed (retry {retry + 1}): {ex.Message}");
                        await Task.Delay(2000 * (retry + 1));
                        
                        // Reset file position for retry
                        fileStream.Position = rangeStart;
                        downloaded = rangeStart;
                    }
                }

                if (!chunkSuccess)
                {
                    throw new Exception($"Failed to download chunk {chunkNumber} after {chunkRetries} retries");
                }
            }

            await fileStream.FlushAsync();
            
            Logger.Log($"Chunked download completed. Total downloaded: {downloaded} bytes");
        }

        /// <summary>
        /// Download using HttpClient (standard fallback)
        /// </summary>
        private async Task DownloadWithHttpClientAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using HttpClient for download");
            
            ResetHttpClient();
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(60));
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Connection.Clear();
            request.Headers.Connection.Add("keep-alive");
            
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            long? totalSize = response.Content.Headers.ContentLength;
            Logger.Log($"Content-Length: {totalSize?.ToString() ?? "unknown"}");
            
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] buffer = new byte[32768];
            long downloaded = 0;
            int bytesRead;
            int progressUpdateCounter = 0;
            DateTime lastProgressTime = DateTime.UtcNow;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                downloaded += bytesRead;
                progressUpdateCounter++;

                if (progressUpdateCounter >= 100 || (DateTime.UtcNow - lastProgressTime).TotalSeconds >= 1)
                {
                    int currentProgress = totalSize.HasValue && totalSize.Value > 0
                        ? baseProgress + (int)((downloaded * (maxProgress - baseProgress)) / totalSize.Value)
                        : baseProgress + Math.Min(maxProgress - baseProgress, (int)(downloaded / (1024 * 1024)));
                    progress?.Report(currentProgress);
                    progressUpdateCounter = 0;
                    lastProgressTime = DateTime.UtcNow;
                }
            }

            await fileStream.FlushAsync(cts.Token);
            
            if (totalSize.HasValue && downloaded != totalSize.Value)
            {
                throw new Exception($"Size mismatch: expected {totalSize.Value}, got {downloaded}");
            }
        }

        private static void RemoveZoneIdentifier(string filePath)
        {
            try
            {
                NativeMethods.DeleteFile(filePath + ":Zone.Identifier");
            }
            catch { }
        }

        private static void LogException(Exception ex, string url, int attempt, int maxRetries)
        {
            Logger.Log($"Error downloading {url} (attempt {attempt}/{maxRetries}): {ex.Message}");
            
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                Logger.Log($"Inner [{depth}]: {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DeleteFile(string lpFileName);
    }
}
