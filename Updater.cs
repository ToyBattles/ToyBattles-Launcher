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

        public async Task<bool> CheckForUpdatesAsync(IProgress<int> progress)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                throw new Exception("No internet connection available.");
            }

            // Check disk space
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

            bool patchLauncherUpdated = await UpdatePatchLauncherAsync(progress);
            if (!patchLauncherUpdated)
            {
                progress.Report(100);
            }

            await UpdatePatchesAsync(progress);

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

        private async Task UpdatePatchesAsync(IProgress<int> progress)
        {
            string? remotePatch = await _downloader.DownloadStringAsync(_config.PatchUrl);
            if (remotePatch == null) return;

            string localPatchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patch.ini");
            string localVersion = "";
            if (File.Exists(localPatchPath))
            {
                string localContent = await File.ReadAllTextAsync(localPatchPath);
                localVersion = GetVersion(localContent);
            }

            string newVersion = GetVersion(remotePatch);
            Logger.Log($"Local version: {localVersion}, Remote version: {newVersion}");

            while (string.Compare(newVersion, localVersion) > 0)
            {
                await _patcher.ApplyPatchAsync(localVersion, newVersion, progress);
                localVersion = await IncrementVersion(localVersion);
            }

            await _downloader.DownloadFileAsync(_config.PatchUrl, localPatchPath, progress, 0, 100);
        }

        private async Task CheckCgdDipAsync()
        {
            string cgdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "cgd.dip");
            if (!File.Exists(cgdPath)) return;

            byte[]? remoteData = await _downloader.DownloadBytesAsync(_config.CgdUrl);
            if (remoteData == null) return;

            byte[] localData = await File.ReadAllBytesAsync(cgdPath);
            uint localChecksum = Adler32(localData);
            uint remoteChecksum = Adler32(remoteData);

            if (remoteChecksum != localChecksum)
            {
                Logger.Log("cgd.dip checksum mismatch. Updating...");
                await File.WriteAllBytesAsync(cgdPath, remoteData);
                Logger.Log("cgd.dip updated successfully.");
            }
            else
            {
                Logger.Log("cgd.dip checksums match.");
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

        private Task<string> IncrementVersion(string version)
        {
            string prefix = "";
            string numPart = version;
            if (version.StartsWith("ENG_"))
            {
                prefix = "ENG_";
                numPart = version.Substring(4);
            }
            var parts = numPart.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[^1], out int last))
            {
                parts[^1] = (last + 1).ToString();
                return Task.FromResult(prefix + string.Join(".", parts));
            }
            return Task.FromResult(version);
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