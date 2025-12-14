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
            // Configure TLS settings globally
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
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
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                    AllowRenegotiation = true
                },
                ConnectTimeout = TimeSpan.FromSeconds(60),
                MaxConnectionsPerServer = 2,
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
            client.DefaultRequestHeaders.Connection.Add("close");
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
            const int maxRetries = 5;
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

                    // Try curl.exe first (uses Windows native TLS, most reliable)
                    if (attempt <= 2)
                    {
                        await DownloadWithCurlAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    // Then try PowerShell
                    else if (attempt <= 4)
                    {
                        await DownloadWithPowerShellAsync(url, filePath, progress, baseProgress, maxProgress);
                    }
                    // Finally try HttpClient
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
        /// Download using curl.exe which is built into Windows 10/11 and uses Windows native TLS
        /// </summary>
        private async Task DownloadWithCurlAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using curl.exe for download");
            
            // Ensure directory exists
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // curl.exe is built into Windows 10 1803+ and Windows 11
            // It uses Windows native TLS (Schannel) which handles the connection properly
            // Don't force TLS version - let curl auto-negotiate
            var psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"--location --fail --silent --show-error --output \"{filePath}\" --retry 3 --retry-delay 2 \"{url}\"",
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

            // Report progress periodically while waiting
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

            // Wait for process with timeout (60 minutes for large files)
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

            // Verify file was downloaded
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
        /// Download using PowerShell's Invoke-WebRequest
        /// </summary>
        private async Task DownloadWithPowerShellAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using PowerShell for download");
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string escapedUrl = url.Replace("'", "''");
            string escapedPath = filePath.Replace("'", "''");
            
            // Use Start-BitsTransfer which uses BITS (Background Intelligent Transfer Service)
            // This is more reliable than Invoke-WebRequest for large files
            string script = $@"
$ProgressPreference = 'SilentlyContinue'
try {{
    Start-BitsTransfer -Source '{escapedUrl}' -Destination '{escapedPath}' -Priority Foreground
}} catch {{
    # Fallback to Invoke-WebRequest if BITS fails
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    Invoke-WebRequest -Uri '{escapedUrl}' -OutFile '{escapedPath}' -UseBasicParsing
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
                        progressValue = Math.Min(maxProgress - 5, progressValue + 1);
                        progress?.Report(progressValue);
                    }
                }
            });

            bool exited = await Task.Run(() => process.WaitForExit(60 * 60 * 1000));
            
            if (!exited)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("PowerShell download timed out");
            }

            await progressTask;
            
            if (process.ExitCode != 0)
            {
                string error = errorBuilder.ToString();
                Logger.Log($"PowerShell error: {error}");
                throw new Exception($"PowerShell download failed: {error}");
            }

            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            {
                throw new Exception("Download failed - file not found or empty");
            }

            progress?.Report(maxProgress);
        }

        /// <summary>
        /// Download using HttpClient (fallback)
        /// </summary>
        private async Task DownloadWithHttpClientAsync(string url, string filePath, IProgress<int>? progress, int baseProgress, int maxProgress)
        {
            Logger.Log("Using HttpClient for download");
            
            ResetHttpClient();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(60));
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Connection.Clear();
            request.Headers.Connection.Add("close");
            
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
