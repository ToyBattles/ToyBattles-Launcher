using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Launcher
{
    public class Patcher
    {
        private readonly Downloader _downloader;
        private readonly Configuration _config;

        public Patcher(Downloader downloader, Configuration config)
        {
            _downloader = downloader;
            _config = config;
        }

        public async Task ApplyPatchAsync(string localVersion, string newVersion, IProgress<int> progress, Action<string>? statusCallback = null)
        {
            // Use the actual target version directly, not an incremented version
            string cabUrl = $"{_config.ServerAddress}/microvolts/{newVersion}/microvolts-{localVersion}-{newVersion}.cab";
            string tempDir = Path.Combine(Path.GetTempPath(), $"LauncherPatch_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            string cabPath = Path.Combine(tempDir, "patch.cab");

            try
            {
                statusCallback?.Invoke("Downloading patch files...");
                await _downloader.DownloadFileAsync(cabUrl, cabPath, progress, 0, 100, statusCallback);
                Logger.Log($"CAB file downloaded to {cabPath}");

                statusCallback?.Invoke("Downloading patch metadata...");
                string xmlUrl = cabUrl.Replace(".cab", ".xml");
                string? xmlContent = await _downloader.DownloadStringAsync(xmlUrl);
                if (xmlContent == null)
                {
                    throw new Exception("Failed to download XML file");
                }

                var checksums = ParseChecksums(xmlContent);

                statusCallback?.Invoke("Extracting patch files...");
                await UnpackCabAsync(cabPath, tempDir, progress);
                progress.Report(100);

                statusCallback?.Invoke("Applying patch files...");
                await ReplaceFilesAsync(tempDir, checksums);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        public async Task<bool> CheckPatchExistsAsync(string fromVersion, string toVersion)
        {
            try
            {
                string cabUrl = $"{_config.ServerAddress}/microvolts/{toVersion}/microvolts-{fromVersion}-{toVersion}.cab";
                string xmlUrl = cabUrl.Replace(".cab", ".xml");
                
                // Try to download just the XML to check if patch exists
                string? xmlContent = await _downloader.DownloadStringAsync(xmlUrl);
                return xmlContent != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> GetAvailablePatchVersionsAsync(string currentVersion)
        {
            var availableVersions = new List<string>();
            
            try
            {
                // Download the main patch.ini file which contains all available versions
                string patchUrl = $"{_config.ServerAddress}/microvolts/patch.ini";
                string? patchContent = await _downloader.DownloadStringAsync(patchUrl);
                
                if (patchContent == null)
                    return availableVersions;
                
                // Parse the patch.ini content to extract all version entries
                var lines = patchContent.Split('\n');
                bool inPatchSection = false;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Check for section headers
                    if (trimmed == "[patch]")
                        inPatchSection = true;
                    else if (trimmed.StartsWith("[") && trimmed != "[patch]")
                        inPatchSection = false;
                    
                    // Parse version entries in the [patch] section
                    else if (inPatchSection && trimmed.StartsWith("version"))
                    {
                        // Extract version value (e.g., "version = ENG_2.0.3.7" -> "ENG_2.0.3.7")
                        var parts = trimmed.Split('=');
                        if (parts.Length >= 2)
                        {
                            string version = parts[1].Trim();
                            // Only include versions newer than current
                            if (string.Compare(version, currentVersion) > 0)
                            {
                                availableVersions.Add(version);
                            }
                        }
                    }
                }
                
                // Sort versions in ascending order
                availableVersions.Sort((a, b) => string.Compare(a, b));
            }
            catch (Exception ex)
            {
                Logger.Log($"Error getting available patch versions: {ex.Message}");
            }
            
            return availableVersions;
        }

        public async Task ApplyStepByStepPatchAsync(string currentVersion, string targetVersion, IProgress<int> progress, Action<string>? statusCallback = null)
        {
            Logger.Log($"Starting step-by-step patch from {currentVersion} to {targetVersion}");
            
            // Get all available versions between current and target
            var availableVersions = await GetAvailablePatchVersionsAsync(currentVersion);
            
            // Filter to only versions up to target
            var versionsToApply = availableVersions
                .Where(v => string.Compare(v, targetVersion) <= 0)
                .ToList();
            
            if (!versionsToApply.Contains(targetVersion))
            {
                versionsToApply.Add(targetVersion);
            }
            
            if (versionsToApply.Count == 0)
            {
                Logger.Log("No patch versions found to apply");
                progress.Report(100);
                return;
            }
            
            string current = currentVersion;
            int totalSteps = versionsToApply.Count;
            int currentStep = 0;
            bool anyPatchApplied = false;
            
            foreach (string nextVersion in versionsToApply)
            {
                currentStep++;
                statusCallback?.Invoke($"Checking patch {currentStep}/{totalSteps}: {current} → {nextVersion}");
                
                // Check if direct patch exists
                bool patchExists = await CheckPatchExistsAsync(current, nextVersion);
                
                if (!patchExists)
                {
                    Logger.Log($"Direct patch from {current} to {nextVersion} not found, skipping");
                    // Still update current version to continue the chain
                    current = nextVersion;
                    continue;
                }
                
                try
                {
                    // Calculate progress range for this step
                    int stepStart = (currentStep - 1) * (100 / totalSteps);
                    int stepEnd = currentStep * (100 / totalSteps);
                    
                    // Create a sub-progress for this step
                    var stepProgress = new Progress<int>(value =>
                    {
                        int overallProgress = stepStart + (value * (stepEnd - stepStart) / 100);
                        progress.Report(overallProgress);
                    });
                    
                    statusCallback?.Invoke($"Applying patch {currentStep}/{totalSteps}: {current} → {nextVersion}");
                    await ApplyPatchAsync(current, nextVersion, stepProgress, statusCallback);
                    current = nextVersion;
                    anyPatchApplied = true;
                    
                    Logger.Log($"Successfully applied patch from {currentVersion} to {nextVersion}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to apply patch from {current} to {nextVersion}: {ex.Message}");
                    
                    // If patch failed, try to continue with next version anyway
                    // This allows the system to skip broken patches and continue
                    current = nextVersion;
                    
                    // Report partial progress
                    progress.Report((currentStep * 100) / totalSteps);
                }
            }
            
            // Update patch.ini with the final version if any patch was applied
            if (anyPatchApplied)
            {
                try
                {
                    // Use the main patch.ini URL, not the version-specific one
                    string patchUrl = $"{_config.ServerAddress}/microvolts/patch.ini";
                    string localPatchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patch.ini");
                    
                    // Download the final patch.ini
                    await _downloader.DownloadFileAsync(patchUrl, localPatchPath, progress, 0, 100, statusCallback);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to download final patch.ini: {ex.Message}");
                }
            }
            
            progress.Report(100);
            statusCallback?.Invoke($"Patch completed: {currentVersion} → {current}");
        }

        private Dictionary<string, string> ParseChecksums(string xmlContent)
        {
            var doc = XDocument.Parse(xmlContent);
            return doc.Descendants("File")
                .Select(f => new { Name = f.Attribute("Name")?.Value, CheckSum = f.Attribute("CheckSum")?.Value })
                .Where(f => f.Name != null && f.CheckSum != null)
                .ToDictionary(f => f.Name!, f => f.CheckSum!);
        }

        private async Task UnpackCabAsync(string cabPath, string tempDir, IProgress<int>? progress = null)
        {
            // Try using expand.exe first
            try
            {
                var expandProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "expand.exe",
                    Arguments = $"\"{cabPath}\" -F:* \"{tempDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (expandProcess != null)
                {
                    Logger.Log("Starting expand.exe...");
                    
                    // Read output asynchronously to avoid deadlocks
                    var outputTask = expandProcess.StandardOutput.ReadToEndAsync();
                    var errorTask = expandProcess.StandardError.ReadToEndAsync();
                    
                    // Report progress during extraction
                    progress?.Report(75); // 75% complete when extraction starts
                    
                    int timeout = CalculateExtractionTimeout(cabPath);
                    Logger.Log($"Using extraction timeout of {timeout / 1000} seconds for CAB file");
                    
                    bool exited = expandProcess.WaitForExit(timeout);
                    
                    if (exited)
                    {
                        Logger.Log($"expand.exe exited with code {expandProcess.ExitCode}");
                        if (expandProcess.ExitCode != 0)
                        {
                            string error = await errorTask;
                            string output = await outputTask;
                            Logger.Log($"expand.exe output: {output}");
                            Logger.Log($"expand.exe error: {error}");
                            throw new Exception($"expand.exe failed with code {expandProcess.ExitCode}: {error}");
                        }
                        else
                        {
                            string output = await outputTask;
                            Logger.Log($"expand.exe output: {output}");
                            progress?.Report(90); // 90% complete after successful extraction
                        }
                    }
                    else
                    {
                        Logger.Log("expand.exe timed out");
                        expandProcess.Kill();
                        throw new Exception("expand.exe timed out while unpacking the patch.");
                    }
                }
                else
                {
                    throw new Exception("Failed to start expand.exe");
                }
            }
            catch (Exception expandEx)
            {
                Logger.Log($"expand.exe failed: {expandEx.Message}");
                Logger.Log("Falling back to .NET ZIP extraction...");
                
                // Fallback: try to extract as ZIP (some CAB files can be treated as ZIP)
                try
                {
                    await ExtractAsZipAsync(cabPath, tempDir, progress);
                }
                catch (Exception zipEx)
                {
                    Logger.Log($"ZIP extraction also failed: {zipEx.Message}");
                    throw new Exception($"Both expand.exe and ZIP extraction failed. Original error: {expandEx.Message}");
                }
            }
        }

        private Task ExtractAsZipAsync(string cabPath, string tempDir, IProgress<int>? progress = null)
        {
            try
            {
                // Try to extract as ZIP archive
                using var archive = ZipFile.OpenRead(cabPath);
                foreach (var entry in archive.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        string fullPath = Path.Combine(tempDir, entry.FullName);
                        string? dirPath = Path.GetDirectoryName(fullPath);
                        if (dirPath != null)
                        {
                            Directory.CreateDirectory(dirPath);
                        }
                        entry.ExtractToFile(fullPath, overwrite: true);
                    }
                }
                progress?.Report(90);
                Logger.Log("Successfully extracted CAB file using ZIP method");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to extract as ZIP: {ex.Message}");
                throw;
            }
            return Task.CompletedTask;
        }

        private int CalculateExtractionTimeout(string cabPath)
        {
            try
            {
                var fileInfo = new FileInfo(cabPath);
                long fileSize = fileInfo.Length;
                
                // Base timeout of 5 minutes, plus 1 minute per 100MB
                int baseTimeout = 300000; // 5 minutes
                int additionalTimeout = (int)(fileSize / (100 * 1024 * 1024)) * 60000; // 1 minute per 100MB
                
                return baseTimeout + additionalTimeout;
            }
            catch
            {
                // Fallback to 10 minutes if we can't determine file size
                return 600000;
            }
        }

        private async Task ReplaceFilesAsync(string tempDir, Dictionary<string, string> checksums)
        {
            var processes = Process.GetProcessesByName("MicroVolts");
            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                }
                catch
                {
                    // Ignore if can't kill
                }
            }
            await Task.Delay(1000);

            string[] extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            foreach (string file in extractedFiles)
            {
                if (Path.GetFileName(file) == "patch.cab") continue;

                string relativePath = Path.GetRelativePath(tempDir, file);
                if (relativePath == @"data\config\ENG\option.ini") continue;
                if (Path.GetFileName(file) == "launcher.txt") continue;

                string fileName = Path.GetFileName(file);
                if (checksums.ContainsKey(fileName))
                {
                    byte[] fileData = await File.ReadAllBytesAsync(file);
                    uint computed = Adler32(fileData);
                    string computedHex = computed.ToString("x8");
                    if (computedHex != checksums[fileName])
                    {
                        Logger.Log($"Checksum mismatch for {fileName}: expected {checksums[fileName]}, got {computedHex}");
                        throw new Exception($"Checksum error for {fileName}");
                    }
                }

                string targetName = Path.GetFileNameWithoutExtension(Path.GetFileName(relativePath));
                string? targetDir = Path.GetDirectoryName(relativePath);
                string target = targetDir != null ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetDir, targetName) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                try
                {
                    using var source = File.OpenRead(file);
                    using var dest = File.Create(target);
                    await source.CopyToAsync(dest);
                }
                catch (IOException ex)
                {
                    Logger.Log($"Failed to replace file {target}: {ex.Message}");
                }
            }
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

        // Test method for step-by-step patching logic
        public async Task TestStepByStepPatchingAsync()
        {
            Logger.Log("Testing step-by-step patching logic...");
            
            // Test version parsing
            string testVersion = "ENG_2.0.3.5";
            var versions = await GetAvailablePatchVersionsAsync(testVersion);
            
            Logger.Log($"Found {versions.Count} available versions starting from {testVersion}");
            foreach (var version in versions.Take(5)) // Show first 5
            {
                Logger.Log($"  - {version}");
            }
            
            // Test patch existence check
            if (versions.Count > 0)
            {
                bool exists = await CheckPatchExistsAsync(testVersion, versions[0]);
                Logger.Log($"Patch from {testVersion} to {versions[0]} exists: {exists}");
            }
            
            Logger.Log("Step-by-step patching test completed");
        }
    }
}