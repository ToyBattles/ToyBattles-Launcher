using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Launcher
{
    public class RepairService
    {
        private readonly Configuration _config;
        private readonly Downloader _downloader;

        public RepairService(Configuration config, Downloader downloader)
        {
            _config = config;
            _downloader = downloader;
        }

        public async Task RepairAsync(IProgress<int> progress)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"LauncherRepair_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "Full.zip");

            try
            {
                // Check disk space
                long? zipSize = await _downloader.GetContentLengthAsync(_config.FullZipUrl);
                if (zipSize.HasValue)
                {
                    string installPath = AppDomain.CurrentDomain.BaseDirectory;
                    string? installRoot = Path.GetPathRoot(installPath);
                    string? tempRoot = Path.GetPathRoot(tempDir);
                    if (installRoot == null || tempRoot == null)
                    {
                        throw new Exception("Unable to determine drive for paths.");
                    }
                    DriveInfo installDrive = new DriveInfo(installRoot);
                    DriveInfo tempDrive = new DriveInfo(tempRoot);
                    long buffer = 100L * 1024 * 1024;
                    long requiredTemp = zipSize.Value + buffer;
                    long requiredInstall = zipSize.Value * 2;
                    if (tempDrive.AvailableFreeSpace < requiredTemp || installDrive.AvailableFreeSpace < requiredInstall)
                    {
                        throw new Exception("Insufficient disk space for repair. Please free up space and try again.");
                    }
                }

                await _downloader.DownloadFileAsync(_config.FullZipUrl, zipPath, progress, 0, 100);

                string extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

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
                        // Ignore
                    }
                }
                await Task.Delay(1000);

                // Check for XignCode issue on Windows before replacing files
                bool installCppTools = false;
                string gameLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameLog.txt");
                if (File.Exists(gameLogPath))
                {
                    string logContent = await File.ReadAllTextAsync(gameLogPath);
                    if (logContent.Contains("XignCode Initialize Failed!"))
                    {
                        installCppTools = true;
                    }
                }

                string[] extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                foreach (string file in extractedFiles)
                {
                    string relativePath = Path.GetRelativePath(extractDir, file);
                    if (relativePath == @"data\config\ENG\option.ini") continue;

                    string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                    try
                    {
                        using var source = File.OpenRead(file);
                        using var dest = File.Create(targetPath);
                        await source.CopyToAsync(dest);
                    }
                    catch (IOException ex)
                    {
                        Logger.Log($"Failed to replace file {targetPath}: {ex.Message}");
                    }
                }

                string localPatchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patch.ini");
                await _downloader.DownloadFileAsync(_config.PatchUrl, localPatchPath, progress, 0, 100);

                // Install C++ development tools if XignCode issue was detected
                if (installCppTools)
                {
                    string installerPath = Path.Combine(tempDir, "vs_buildtools.exe");
                    try
                    {
                        await _downloader.DownloadFileAsync("https://aka.ms/vs/17/release/vs_buildtools.exe", installerPath, progress, 0, 100);
                        ProcessStartInfo psi = new(installerPath)
                        {
                            Arguments = "--add Microsoft.VisualStudio.Workload.NativeDesktop --quiet --norestart",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        Logger.Log("Started installation of Visual Studio Build Tools with Desktop development with C++ workload for XignCode fix.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to install C++ tools: {ex.Message}");
                    }
                }
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}