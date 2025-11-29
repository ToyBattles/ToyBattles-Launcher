using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Launcher
{
    public class Downloader
    {
        private readonly HttpClient _httpClient;

        public Downloader()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
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
            try
            {
                Logger.Log($"Starting download of {Path.GetFileName(filePath)} from {url} to {filePath}");

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalSize = response.Content.Headers.ContentLength;
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
                Logger.Log($"Download of {Path.GetFileName(filePath)} completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error downloading file from {url}: {ex.Message}");
                throw;
            }
        }
    }
}