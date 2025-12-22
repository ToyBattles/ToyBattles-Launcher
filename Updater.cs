using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Launcher
{
    public class Updater
    {
        private readonly Configuration _config;
        private readonly Downloader _downloader;
        private readonly Patcher _patcher;

        public Updater(Configuration config, Downloader downloader, Patcher patcher)
        {
            _config = config;
            _downloader = downloader;
            _patcher = patcher;
        }

        public async Task<bool> CheckForUpdatesAsync(IProgress<int> progress, Action<string>? statusCallback = null)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                throw new Exception("No internet connection available.");
            }

            // Check disk space
            statusCallback?.Invoke("Checking disk space...");
            string installPath = AppDomain.CurrentDomain.BaseDirectory;
            string? pathRoot = Path.GetPathRoot(installPath);
            if (pathRoot == null)
            {
                throw new Exception("Unable to determine drive for installation path.");
            }
            DriveInfo installDrive = new DriveInfo(pathRoot);
            if (installDrive.AvailableFreeSpace < 500L * 1024 * 1024)
            {
                throw new Exception("Insufficient disk space for updates. Please free up space and try again.");
            }

            statusCallback?.Invoke("Checking launcher updates...");
            bool patchLauncherUpdated = await UpdatePatchLauncherAsync(progress);
            if (!patchLauncherUpdated)
            {
                progress.Report(100);
            }

            statusCallback?.Invoke("Checking game updates...");
            await UpdatePatchesAsync(progress, statusCallback);

            statusCallback?.Invoke("Verifying game data...");
            await CheckCgdDipAsync();

            return true;
        }

        private async Task<bool> UpdatePatchLauncherAsync(IProgress<int> progress)
        {
            string? remotePatchLauncher = await _downloader.DownloadStringAsync(_config.PatchLauncherUrl);
            if (remotePatchLauncher == null) return false;

            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patchLauncher.ini");
            string localPatchLauncherContent = "";
            bool patchLauncherExists = File.Exists(localPath);
            if (patchLauncherExists)
            {
                localPatchLauncherContent = await File.ReadAllTextAsync(localPath);
            }

            if (patchLauncherExists && localPatchLauncherContent == remotePatchLauncher)
            {
                progress.Report(100);
                return false;
            }

            await File.WriteAllTextAsync(localPath, remotePatchLauncher);
            return true;
        }

        private async Task UpdatePatchesAsync(IProgress<int> progress, Action<string>? statusCallback = null)
        {
            string? remotePatch = await _downloader.DownloadStringAsync(_config.PatchUrl);
            if (remotePatch == null) return;

            string localPatchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patch.ini");
            string localVersion = "";
            string localContent = "";
            if (File.Exists(localPatchPath))
            {
                localContent = await File.ReadAllTextAsync(localPatchPath);
                localVersion = GetVersion(localContent);
            }

            string newVersion = GetVersion(remotePatch);
            Logger.Log($"Local version: {localVersion}, Remote version: {newVersion}");

            // Check if versions differ - need to apply patch
            if (!string.IsNullOrEmpty(localVersion) && string.Compare(newVersion, localVersion) > 0)
            {
                Logger.Log($"New version available, applying step-by-step patch from {localVersion} to {newVersion}");
                statusCallback?.Invoke($"Applying step-by-step patch {localVersion} → {newVersion}...");
                await _patcher.ApplyStepByStepPatchAsync(localVersion, newVersion, progress, statusCallback);
                statusCallback?.Invoke("Downloading patch info...");
                await _downloader.DownloadFileAsync(_config.PatchUrl, localPatchPath, progress, 0, 100, statusCallback);
            }
            // Same version - check if patch content changed by checksum
            else if (!string.IsNullOrEmpty(localVersion) && localVersion == newVersion)
            {
                uint localChecksum = Adler32(System.Text.Encoding.UTF8.GetBytes(localContent));
                uint remoteChecksum = Adler32(System.Text.Encoding.UTF8.GetBytes(remotePatch));

                if (localChecksum != remoteChecksum)
                {
                    Logger.Log($"Same version ({localVersion}) but patch.ini content changed. Local checksum: {localChecksum:x8}, Remote checksum: {remoteChecksum:x8}. Re-applying patch...");
                    statusCallback?.Invoke($"Re-applying patch {localVersion}...");
                    await _patcher.ApplyPatchAsync(localVersion, newVersion, progress, statusCallback);
                    statusCallback?.Invoke("Downloading patch info...");
                    await _downloader.DownloadFileAsync(_config.PatchUrl, localPatchPath, progress, 0, 100, statusCallback);
                }
                else
                {
                    Logger.Log($"Version {localVersion} is up to date, checksums match.");
                    progress.Report(100);
                }
            }
            else
            {
                // No local version, just download the patch.ini
                statusCallback?.Invoke("Downloading patch info...");
                await _downloader.DownloadFileAsync(_config.PatchUrl, localPatchPath, progress, 0, 100, statusCallback);
            }
        }

        private async Task CheckCgdDipAsync()
        {
            string cgdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "cgd.dip");
            if (!File.Exists(cgdPath))
            {
                Logger.Log("cgd.dip not found locally, skipping check.");
                return;
            }

            Logger.Log($"Checking cgd.dip from {_config.CgdUrl}");
            byte[]? remoteData = await _downloader.DownloadBytesAsync(_config.CgdUrl);
            if (remoteData == null)
            {
                Logger.Log("Failed to download remote cgd.dip, skipping check.");
                return;
            }

            byte[] localData = await File.ReadAllBytesAsync(cgdPath);
            uint localChecksum = Adler32(localData);
            uint remoteChecksum = Adler32(remoteData);

            Logger.Log($"cgd.dip - Local size: {localData.Length} bytes, Remote size: {remoteData.Length} bytes");
            Logger.Log($"cgd.dip - Local checksum: {localChecksum:x8}, Remote checksum: {remoteChecksum:x8}");

            if (remoteChecksum != localChecksum)
            {
                Logger.Log("cgd.dip checksum mismatch. Updating...");
                await File.WriteAllBytesAsync(cgdPath, remoteData);
                Logger.Log("cgd.dip updated successfully.");
            }
            else
            {
                Logger.Log("cgd.dip checksums match, no update needed.");
            }
        }

        private string GetVersion(string iniContent)
        {
            var lines = iniContent.Split('\n');
            bool inPatch = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed == "[patch]") inPatch = true;
                else if (trimmed.StartsWith("[") && trimmed != "[patch]") inPatch = false;
                else if (inPatch && trimmed.StartsWith("version = "))
                {
                    return trimmed.Split('=')[1].Trim();
                }
            }
            return "";
        }

        private static uint Adler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;
            foreach (byte bt in data)
            {
                a = (a + bt) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }
            return (b << 16) | a;
        }
    }
}