using System;
using System.IO;
using System.Linq;

namespace Launcher
{
    public class Configuration
    {
        public bool IsInstalled => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updateinfo.ini"));
        private string? _serverAddress;
        public string ServerAddress
        {
            get
            {
                if (_serverAddress == null)
                {
                    _serverAddress = GetServerAddress();
                }
                return _serverAddress;
            }
        }
        public string PatchLauncherUrl => $"{ServerAddress}/microvolts/Patcher/patchLauncher.ini";
        public string PatchUrl => $"{ServerAddress}/microvolts/patch.ini";
        public string CgdUrl => $"{ServerAddress}/microvolts/Full/data/cgd.dip";
        public string FullZipUrl => $"{ServerAddress}/microvolts/Full/Full.zip";
        public static string DiscordUrl => "https://discord.gg/CMAG9qzXFh";
        public static string WebsiteUrl => "https://toybattles.net";

        public Configuration()
        {
        }

        private string GetServerAddress()
        {
            string updateInfoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updateinfo.ini");
            if (File.Exists(updateInfoPath))
            {
                string iniContent = File.ReadAllText(updateInfoPath);
                string[] lines = iniContent.Split('\n');
                bool isInUpdateSection = false;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed == "[update]") isInUpdateSection = true;
                    else if (trimmed.StartsWith("[") && trimmed != "[update]") isInUpdateSection = false;
                    else if (isInUpdateSection && trimmed.Contains("addr = "))
                    {
                        string addr = trimmed.Split('=')[1].Trim();
                        if (addr.StartsWith("http://"))
                        {
                            addr = string.Concat("https://", addr.AsSpan(7));
                        }
                        return addr;
                    }
                }
            }
            return "https://cdn.toybattles.net/ENG";
        }
    }
}