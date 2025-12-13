using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Launcher
{
    public class Downloader
    {
        private HttpClient _httpClient;

        public Downloader()
        {
            // Configure TLS settings for Windows 11 compatibility
            // This fixes "The decryption operation failed" errors
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            
            _httpClient = CreateHttpClient();
        }

        private static HttpClient CreateHttpClient()
        {
            // Use SocketsHttpHandler for better TLS handling on .NET 8
            var handler = new SocketsHttpHandler
            {
                // Allow automatic decompression
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                // Disable connection pooling to avoid stale connections causing TLS issues
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                // Configure SSL/TLS settings
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    // Custom certificate validation to handle potential certificate issues
                    RemoteCertificateValidationCallback = ValidateServerCertificate,
                    // Allow renegotiation
                    AllowRenegotiation = true,
                    // Use default cipher suites
                    CipherSuitesPolicy = null
                },
                // Connection settings
                ConnectTimeout = TimeSpan.FromSeconds(30),
                // Keep-alive settings
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                // Max connections per server
                MaxConnectionsPerServer = 4
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30), // Increased timeout for large files
                // Force HTTP/1.1 to avoid potential HTTP/2 TLS issues
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.ConnectionClose = false;
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            client.DefaultRequestHeaders.ExpectContinue = false;
            
            return client;
        }

        private void ResetHttpClient()
        {
            // Dispose old client and create a fresh one to clear any bad connection state
            var oldClient = _httpClient;
            _httpClient = CreateHttpClient();
            try { oldClient.Dispose(); } catch { }
            Logger.Log("HttpClient reset due to connection issues");
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            // Log certificate issues for debugging but allow the connection
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                Logger.Log($"SSL Certificate warning: {sslPolicyErrors}");
                if (certificate != null)
                {
                    Logger.Log($"Certificate subject: {certificate.Subject}");
                    Logger.Log($"Certificate issuer: {certificate.Issuer}");
                }
            }
            // Return true to accept the certificate (be cautious with this in production)
            return true;
        }

        public async Task<string?> DownloadStringAsync(string url)
        {
            try
            {
                return await _httpClient.GetStringAsync(url);
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
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalSize = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var memoryStream = new MemoryStream();

                byte[] buffer = new byte[1048576];
                long downloaded = 0;
                int bytesRead;
                int progressUpdateCounter = 0;
                const int PROGRESS_UPDATE_INTERVAL = 50;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead);
                    downloaded += bytesRead;
                    progressUpdateCounter++;

                    if (progressUpdateCounter >= PROGRESS_UPDATE_INTERVAL || downloaded == totalSize)
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
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
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
            const int maxRetries = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Logger.Log($"Starting download of {Path.GetFileName(filePath)} from {url} to {filePath}");

                    // Delete partial file if exists from previous attempt
                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    // Add cache-busting header to avoid cached bad responses
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? totalSize = response.Content.Headers.ContentLength;
                    Logger.Log($"Content-Length: {totalSize?.ToString() ?? "unknown"}");
                    
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true);

                    byte[] buffer = new byte[1048576];
                    long downloaded = 0;
                    int bytesRead;
                    int progressUpdateCounter = 0;
                    const int PROGRESS_UPDATE_INTERVAL = 50;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloaded += bytesRead;
                        progressUpdateCounter++;

                        if (progressUpdateCounter >= PROGRESS_UPDATE_INTERVAL || downloaded == totalSize)
                        {
                            int currentProgress = totalSize.HasValue && totalSize.Value > 0
                                ? baseProgress + (int)((downloaded * (maxProgress - baseProgress)) / totalSize.Value)
                                : baseProgress + Math.Min(maxProgress - baseProgress, (int)(downloaded / 1024));
                            progress?.Report(currentProgress);
                            progressUpdateCounter = 0;
                        }
                    }

                    await fileStream.FlushAsync();
                    Logger.Log($"Download of {Path.GetFileName(filePath)} completed successfully. Downloaded {downloaded} bytes.");
                    return; // Success, exit the retry loop
                }
                catch (Exception ex) when (IsRetryableException(ex) && attempt < maxRetries)
                {
                    lastException = ex;
                    Logger.Log($"Error downloading file from {url} (attempt {attempt}/{maxRetries}): {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Logger.Log($"Inner exception: {ex.InnerException.Message}");
                    }
                    
                    // Reset HttpClient to clear any bad connection state
                    ResetHttpClient();
                    
                    // Wait before retrying with exponential backoff
                    int delayMs = 2000 * (int)Math.Pow(2, attempt - 1); // 2s, 4s, 8s
                    Logger.Log($"Retrying in {delayMs / 1000} seconds...");
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error downloading file from {url}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Logger.Log($"Inner exception: {ex.InnerException.Message}");
                    }
                    throw;
                }
            }

            // If we get here, all retries failed
            Logger.Log($"All {maxRetries} download attempts failed for {url}");
            throw lastException ?? new Exception($"Failed to download {url} after {maxRetries} attempts");
        }

        private static bool IsRetryableException(Exception ex)
        {
            // Check for TLS/SSL errors (decryption failed)
            if (ex.Message.Contains("decryption", StringComparison.OrdinalIgnoreCase))
                return true;
            
            if (ex.Message.Contains("dekryptera", StringComparison.OrdinalIgnoreCase)) // Swedish
                return true;
            
            // Check for network-related errors
            if (ex is HttpRequestException)
                return true;
            
            // Check for IO errors during download
            if (ex is IOException)
                return true;
            
            // Check inner exceptions
            if (ex.InnerException != null)
                return IsRetryableException(ex.InnerException);
            
            return false;
        }
    }
}
