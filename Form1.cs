using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Launcher
{
    public partial class Form1 : Form
    {
        #region Fields

        // Services
        private readonly Configuration _config;
        private readonly Downloader _downloader;
        private readonly Patcher _patcher;
        private readonly Updater _updater;
        private readonly RepairService _repairService;

        // State
        private volatile bool _isLoading;
        private readonly bool _autoStart;
        private volatile bool _isMinimizedToTray;
        private volatile bool _updateAvailable;
        private volatile bool _runInBackground = true;

        // UI Animation
        private readonly System.Windows.Forms.Timer _animateTimer;
        private readonly System.Windows.Forms.Timer _timeTimer;
        private readonly System.Windows.Forms.Timer _updateCheckTimer;
        private int _targetValue;
        private string _currentText = string.Empty;
        private bool _enableButtonAtEnd;

        // Labels
        private readonly Label _progressLabel;
        private readonly Label _timeLabel;

        // Notification
        private CancellationTokenSource? _notificationCts;
        private Form? _updateNotificationForm;

        // Cached images for better performance
        private Bitmap? _startNormalImage;
        private Bitmap? _startDisabledImage;
        private Bitmap? _startClickImage;
        private Bitmap? _exitNormalImage;
        private Bitmap? _exitClickImage;
        private Bitmap? _repairNormalImage;
        private Bitmap? _repairDisabledImage;
        private Bitmap? _repairClickImage;
        private Bitmap? _discordNormalImage;
        private Bitmap? _discordClickImage;
        private Bitmap? _webNormalImage;
        private Bitmap? _webClickImage;

        // Cached checksum
        private string? _launcherChecksum;

        #endregion

        #region Native Methods

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        #endregion

        #region Constructors

        public Form1(string[] args) : this()
        {
            _autoStart = args.Contains("--autostart");
        }

        public Form1()
        {
            // Initialize services
            _config = new Configuration();
            _downloader = new Downloader();
            _patcher = new Patcher(_downloader, _config);
            _updater = new Updater(_config, _downloader, _patcher);
            _repairService = new RepairService(_config, _downloader);

            InitializeComponent();
            InitializeCachedImages();
            SetupForm();
            SetupEventHandlers();

            // Setup timers
            _animateTimer = CreateTimer(50, AnimateTimer_Tick);
            _timeTimer = CreateTimer(1000, TimeTimer_Tick);
            _updateCheckTimer = CreateTimer(5 * 60 * 1000, UpdateCheckTimer_Tick); // 5 minutes

            // Setup labels
            _progressLabel = CreateProgressLabel();
            _timeLabel = CreateTimeLabel();

            // Setup button parents for transparency
            SetupButtonParents();

            // Initial button state
            SetStartButtonState(false);

            // Start time timer
            _timeTimer.Start();
        }

        #endregion

        #region Initialization

        private void InitializeCachedImages()
        {
            // Cache all button images on startup for better performance
            Task.Run(() =>
            {
                _startNormalImage = new Bitmap(Properties.Resources.startNormal, startButton.Width, startButton.Height);
                _startDisabledImage = new Bitmap(Properties.Resources.startDisabled, startButton.Width, startButton.Height);
                _startClickImage = new Bitmap(Properties.Resources.startClick, startButton.Width, startButton.Height);
                _exitNormalImage = new Bitmap(Properties.Resources.exitNormal, exitButton.Width, exitButton.Height);
                _exitClickImage = new Bitmap(Properties.Resources.exitClick, exitButton.Width, exitButton.Height);
                _repairNormalImage = new Bitmap(Properties.Resources.repairNormal, RepairButton.Width, RepairButton.Height);
                _repairDisabledImage = new Bitmap(Properties.Resources.repairDisabled, RepairButton.Width, RepairButton.Height);
                _repairClickImage = new Bitmap(Properties.Resources.repairClick, RepairButton.Width, RepairButton.Height);
                _discordNormalImage = new Bitmap(Properties.Resources.discordNormal, DiscordButton.Width, DiscordButton.Height);
                _discordClickImage = new Bitmap(Properties.Resources.discordClick, DiscordButton.Width, DiscordButton.Height);
                _webNormalImage = new Bitmap(Properties.Resources.webNormal, WebsiteButton.Width, WebsiteButton.Height);
                _webClickImage = new Bitmap(Properties.Resources.webClick, WebsiteButton.Width, WebsiteButton.Height);
            });
        }

        private void SetupForm()
        {
            Text = "ToyBattles Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            bgPicture.SizeMode = PictureBoxSizeMode.StretchImage;
            // Don't use Dock.Fill as it changes bgPicture position and affects child controls
            // bgPicture size is already set correctly in Designer
        }

        private void SetupEventHandlers()
        {
            // Form events
            MouseDown += Form1_MouseDown;
            bgPicture.MouseDown += Form1_MouseDown;

            // Button events
            startButton.MouseEnter += StartButton_MouseEnter;
            startButton.MouseLeave += StartButton_MouseLeave;
            startButton.Click += StartButton_Click;

            exitButton.MouseEnter += ExitButton_MouseEnter;
            exitButton.MouseLeave += ExitButton_MouseLeave;
            exitButton.Click += ExitButton_Click;

            DiscordButton.Click += DiscordButton_Click;
            DiscordButton.MouseEnter += DiscordButton_MouseEnter;
            DiscordButton.MouseLeave += DiscordButton_MouseLeave;

            WebsiteButton.Click += WebsiteButton_Click;
            WebsiteButton.MouseEnter += WebsiteButton_MouseEnter;
            WebsiteButton.MouseLeave += WebsiteButton_MouseLeave;

            RepairButton.MouseEnter += RepairButton_MouseEnter;
            RepairButton.MouseLeave += RepairButton_MouseLeave;
            RepairButton.Click += RepairButton_Click;
        }

        private void SetupButtonParents()
        {
            // bgPicture is at (2, -1), so we need to adjust positions when moving to bgPicture
            int offsetX = 2;
            int offsetY = -1;
            
            // Move buttons to bgPicture for proper transparency
            // exitButton: Designer position (975, 48)
            exitButton.Parent = bgPicture;
            exitButton.Location = new Point(975 - offsetX, 48 - offsetY);
            
            // DiscordButton: Designer position (932, 462)
            DiscordButton.Parent = bgPicture;
            DiscordButton.Location = new Point(932 - offsetX, 462 - offsetY);
            
            // WebsiteButton: Designer position (885, 462)
            WebsiteButton.Parent = bgPicture;
            WebsiteButton.Location = new Point(885 - offsetX, 462 - offsetY);
            
            // RepairButton: Designer position (838, 462)
            RepairButton.Parent = bgPicture;
            RepairButton.Location = new Point(838 - offsetX, 462 - offsetY);
        }

        private static System.Windows.Forms.Timer CreateTimer(int interval, EventHandler handler)
        {
            var timer = new System.Windows.Forms.Timer { Interval = interval };
            timer.Tick += handler;
            return timer;
        }

        private Label CreateProgressLabel()
        {
            var label = new Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.Black,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill
            };
            progressBar1.Controls.Add(label);
            return label;
        }

        private Label CreateTimeLabel()
        {
            // bgPicture is at (2, -1), so we need to adjust positions
            int offsetX = 2;
            int offsetY = -1;
            
            // Time label - add to bgPicture for proper transparency
            var label = new Label
            {
                Location = new Point(800 - offsetX, 40 - offsetY),
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.Yellow,
                Font = new Font("Segoe UI", 14F)
            };
            bgPicture.Controls.Add(label);
            label.BringToFront();

            // Version label - add to bgPicture for proper transparency
            launcherversion.BackColor = Color.Transparent;
            launcherversion.ForeColor = Color.Yellow;
            launcherversion.Location = new Point(72 - offsetX, 48 - offsetY);
            launcherversion.AutoSize = true;
            launcherversion.Text = GetLauncherChecksum();
            launcherversion.Parent = bgPicture;
            launcherversion.BringToFront();

            return label;
        }

        #endregion

        #region Form Lifecycle

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_isLoading)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
            else
            {
                CleanupResources();
                base.OnFormClosing(e);
            }
        }

        private void CleanupResources()
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            _notificationCts?.Cancel();
            _notificationCts?.Dispose();
            
            // Dispose cached images
            _startNormalImage?.Dispose();
            _startDisabledImage?.Dispose();
            _startClickImage?.Dispose();
            _exitNormalImage?.Dispose();
            _exitClickImage?.Dispose();
            _repairNormalImage?.Dispose();
            _repairDisabledImage?.Dispose();
            _repairClickImage?.Dispose();
            _discordNormalImage?.Dispose();
            _discordClickImage?.Dispose();
            _webNormalImage?.Dispose();
            _webClickImage?.Dispose();
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("This application must be run as administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            if (_isLoading) return;
            _isLoading = true;

            SetRepairButtonState(false);
            BackColor = Color.White;
            bgPicture.Image = Properties.Resources.Launcher_BG;

            try
            {
                if (_config.IsInstalled)
                {
                    await PerformUpdateCheckAsync();
                }
                else
                {
                    await HandleFirstInstallAsync();
                }
            }
            finally
            {
                SetRepairButtonState(true);
                _isLoading = false;
            }
        }

        private static bool IsRunningAsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
        }

        #endregion

        #region Tray Icon

        private void MinimizeToTray()
        {
            Hide();
            trayIcon.Visible = true;
            _isMinimizedToTray = true;

            if (_runInBackground && !_updateCheckTimer.Enabled)
            {
                _updateCheckTimer.Start();
            }

            trayIcon.ShowBalloonTip(3000, "ToyBattles Launcher", 
                "Launcher is running in the background. Double-click to open.", ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            trayIcon.Visible = false;
            _isMinimizedToTray = false;
        }

        private void TrayIcon_DoubleClick(object? sender, EventArgs e) => RestoreFromTray();

        private void ShowMenuItem_Click(object? sender, EventArgs e) => RestoreFromTray();

        private void RunInBackgroundMenuItem_Click(object? sender, EventArgs e)
        {
            _runInBackground = runInBackgroundMenuItem.Checked;

            if (!_runInBackground)
            {
                _updateCheckTimer.Stop();
            }
            else if (_isMinimizedToTray)
            {
                _updateCheckTimer.Start();
            }
        }

        private void StopLauncherMenuItem_Click(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            _updateCheckTimer.Stop();
            _notificationCts?.Cancel();
            Application.Exit();
        }

        #endregion

        #region Update Checking

        private async void UpdateCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMinimizedToTray || _isLoading) return;

            try
            {
                bool hasUpdate = await Task.Run(() => CheckForUpdateSilentlyAsync());
                if (hasUpdate && !_updateAvailable)
                {
                    _updateAvailable = true;
                    ShowUpdateNotification();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Background update check failed: {ex.Message}");
            }
        }

        private async Task<bool> CheckForUpdateSilentlyAsync()
        {
            try
            {
                string localPatchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patch.ini");

                if (!File.Exists(localPatchPath)) return false;

                // Start both tasks concurrently
                var remotePatchTask = _downloader.DownloadStringAsync(_config.PatchUrl);
                var localContentTask = File.ReadAllTextAsync(localPatchPath);
                
                // Await both tasks
                string? remotePatch = await remotePatchTask;
                string localContent = await localContentTask;
                
                if (remotePatch == null) return false;

                string localVersion = ParseVersionFromIni(localContent);
                string remoteVersion = ParseVersionFromIni(remotePatch);

                return string.Compare(remoteVersion, localVersion, StringComparison.Ordinal) > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ParseVersionFromIni(string iniContent)
        {
            bool inPatch = false;
            foreach (var line in iniContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed == "[patch]")
                {
                    inPatch = true;
                }
                else if (trimmed.StartsWith("[") && trimmed != "[patch]")
                {
                    inPatch = false;
                }
                else if (inPatch && trimmed.StartsWith("version = "))
                {
                    return trimmed.Split('=')[1].Trim();
                }
            }
            return string.Empty;
        }

        private async Task PerformUpdateCheckAsync()
        {
            try
            {
                var progress = new Progress<int>(value => UpdateProgress(value, $"Updating... ({value}%)"));
                bool updated = await _updater.CheckForUpdatesAsync(progress);
                
                if (updated)
                {
                    UpdateProgress(100, "Ready to launch");
                    await Task.Delay(1000);
                }

                _enableButtonAtEnd = true;
                if (progressBar1.Value >= 100)
                {
                    SetStartButtonState(true);
                    _enableButtonAtEnd = false;
                    if (_autoStart) StartGame();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during update: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task HandleFirstInstallAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetPathRoot(baseDir)!;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (baseDir == root || baseDir.Equals(desktopPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Please move the launcher to a folder before installing the game.", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateProgress(100, "Failed to Start - Please move the launcher to a folder.");
                SetStartButtonState(false);
            }
            else
            {
                var result = MessageBox.Show(this, 
                    "ToyBattles is not installed. Do you want to install ToyBattles? This is the first time installing the game.", 
                    "Install Game", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    await InstallGameAsync();
                }
                else
                {
                    UpdateProgress(100, "Failed to Start - Please use the repair option.");
                    SetStartButtonState(false);
                }
            }
        }

        #endregion

        #region Update Notification

        private void ShowUpdateNotification()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ShowUpdateNotification));
                return;
            }

            _notificationCts?.Cancel();
            _notificationCts = new CancellationTokenSource();

            _updateNotificationForm = CreateNotificationForm();
            _updateNotificationForm.Show();

            _ = RunNotificationCountdownAsync(_notificationCts.Token);
        }

        private Form CreateNotificationForm()
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(350, 120),
                BackColor = Color.FromArgb(30, 30, 30),
                TopMost = true,
                ShowInTaskbar = false
            };

            var workingArea = Screen.PrimaryScreen!.WorkingArea;
            form.Location = new Point(
                workingArea.Right - form.Width - 20,
                workingArea.Bottom - form.Height - 20
            );

            var titleLabel = new Label
            {
                Text = "ToyBattles Update Available",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(15, 10),
                AutoSize = true
            };

            var messageLabel = new Label
            {
                Text = "A new update is available. Would you like to update now?",
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(15, 35),
                Size = new Size(320, 20)
            };

            var countdownLabel = new Label
            {
                Name = "countdownLabel",
                Text = "Auto-dismiss in 10 seconds...",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(15, 95),
                AutoSize = true
            };

            var yesButton = CreateNotificationButton("Update Now", Color.FromArgb(0, 122, 204), 
                new Point(120, 58), async () =>
            {
                _notificationCts?.Cancel();
                _updateNotificationForm?.Close();
                await StartUpdateProcessAsync();
            });

            var noButton = CreateNotificationButton("Later", Color.FromArgb(60, 60, 60), 
                new Point(230, 58), () =>
            {
                _notificationCts?.Cancel();
                _updateNotificationForm?.Close();
            });

            form.Controls.AddRange(new Control[] { titleLabel, messageLabel, countdownLabel, yesButton, noButton });
            return form;
        }

        private static Button CreateNotificationButton(string text, Color backColor, Point location, Action onClick)
        {
            var button = new Button
            {
                Text = text,
                ForeColor = Color.White,
                BackColor = backColor,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(text == "Later" ? 80 : 100, 30),
                Location = location
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => onClick();
            return button;
        }

        private async Task RunNotificationCountdownAsync(CancellationToken token)
        {
            try
            {
                var countdownLabel = _updateNotificationForm?.Controls["countdownLabel"] as Label;
                if (countdownLabel == null) return;

                for (int i = 10; i > 0; i--)
                {
                    if (token.IsCancellationRequested) return;

                    UpdateLabelText(countdownLabel, $"Auto-dismiss in {i} seconds...");
                    await Task.Delay(1000, token);
                }

                if (!token.IsCancellationRequested)
                {
                    CloseNotificationForm();
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when user clicks a button
            }
        }

        private void UpdateLabelText(Label label, string text)
        {
            if (label.InvokeRequired)
            {
                label.Invoke(new Action(() => label.Text = text));
            }
            else
            {
                label.Text = text;
            }
        }

        private void CloseNotificationForm()
        {
            if (_updateNotificationForm == null) return;

            if (_updateNotificationForm.InvokeRequired)
            {
                _updateNotificationForm.Invoke(new Action(() => _updateNotificationForm.Close()));
            }
            else
            {
                _updateNotificationForm.Close();
            }
        }

        private async Task StartUpdateProcessAsync()
        {
            if (!await CloseGameIfRunningAsync()) return;

            RestoreFromTray();
            _updateAvailable = false;
            _isLoading = true;

            SetStartButtonState(false);
            SetRepairButtonState(false);

            try
            {
                var progress = new Progress<int>(value => UpdateProgress(value, $"Updating... ({value}%)"));
                await _updater.CheckForUpdatesAsync(progress);
                UpdateProgress(100, "Update complete! Ready to launch.");
                await Task.Delay(1000);
                SetStartButtonState(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during update: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRepairButtonState(true);
                _isLoading = false;
            }
        }

        private async Task<bool> CloseGameIfRunningAsync()
        {
            var processes = Process.GetProcessesByName("MicroVolts");
            if (processes.Length == 0) return true;

            var result = MessageBox.Show(
                "MicroVolts is currently running. It needs to be closed to apply the update. Close it now?",
                "Close Game",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes) return false;

            await Task.Run(() =>
            {
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to close MicroVolts: {ex.Message}");
                    }
                }
            });

            return true;
        }

        #endregion

        #region Progress Updates

        private void UpdateProgress(int value, string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int, string>(UpdateProgress), value, text);
                return;
            }

            if (value == 100)
            {
                progressBar1.Value = 100;
                _progressLabel.Text = text;
                progressBar1.Refresh();
            }
            else
            {
                string baseText = text;
                if (text.Contains("(") && text.Contains("%)"))
                {
                    int start = text.LastIndexOf("(");
                    baseText = text[..start].Trim();
                }

                _targetValue = value;
                _currentText = baseText;

                if (progressBar1.Value < _targetValue)
                {
                    _animateTimer.Start();
                }
                else
                {
                    progressBar1.Value = value;
                    _progressLabel.Text = text;
                    progressBar1.Invalidate();
                }
            }
        }

        private void AnimateTimer_Tick(object? sender, EventArgs e)
        {
            progressBar1.Value = Math.Min(progressBar1.Value + 1, _targetValue);
            _progressLabel.Text = $"{_currentText} ({progressBar1.Value}%)";
            progressBar1.Invalidate();

            if (progressBar1.Value >= _targetValue)
            {
                _animateTimer.Stop();
                if (_enableButtonAtEnd && progressBar1.Value >= 100)
                {
                    SetStartButtonState(true);
                    _enableButtonAtEnd = false;
                }
            }
        }

        #endregion

        #region Button State Management

        private void SetStartButtonState(bool enabled)
        {
            startButton.Enabled = enabled;
            startButton.Image = enabled ? _startNormalImage : _startDisabledImage;
        }

        private void SetRepairButtonState(bool enabled)
        {
            RepairButton.Enabled = enabled;
            RepairButton.Image = enabled ? _repairNormalImage : _repairDisabledImage;
        }

        #endregion

        #region Button Event Handlers

        private void Form1_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
        }

        private void StartButton_MouseEnter(object? sender, EventArgs e) => 
            startButton.Image = _startClickImage;

        private void StartButton_MouseLeave(object? sender, EventArgs e) => 
            startButton.Image = _startNormalImage;

        private void StartButton_Click(object? sender, EventArgs e)
        {
            if (startButton.Enabled) StartGame();
        }

        private void ExitButton_MouseEnter(object? sender, EventArgs e) => 
            exitButton.Image = _exitClickImage;

        private void ExitButton_MouseLeave(object? sender, EventArgs e) => 
            exitButton.Image = _exitNormalImage;

        private void ExitButton_Click(object? sender, EventArgs e) => MinimizeToTray();

        private void DiscordButton_MouseEnter(object? sender, EventArgs e) => 
            DiscordButton.Image = _discordClickImage;

        private void DiscordButton_MouseLeave(object? sender, EventArgs e) => 
            DiscordButton.Image = _discordNormalImage;

        private void DiscordButton_Click(object? sender, EventArgs e) => 
            OpenUrl(Configuration.DiscordUrl);

        private void WebsiteButton_MouseEnter(object? sender, EventArgs e) => 
            WebsiteButton.Image = _webClickImage;

        private void WebsiteButton_MouseLeave(object? sender, EventArgs e) => 
            WebsiteButton.Image = _webNormalImage;

        private void WebsiteButton_Click(object? sender, EventArgs e) => 
            OpenUrl(Configuration.WebsiteUrl);

        private void RepairButton_MouseEnter(object? sender, EventArgs e) => 
            RepairButton.Image = _repairClickImage;

        private void RepairButton_MouseLeave(object? sender, EventArgs e) => 
            RepairButton.Image = _repairNormalImage;

        private async void RepairButton_Click(object? sender, EventArgs e)
        {
            if (!RepairButton.Enabled) return;

            SetRepairButtonState(false);

            var result = MessageBox.Show(this, 
                "Are you sure you want to repair the game? This will redownload all game files.", 
                "Confirm Repair", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                await Task.Delay(3000);
                SetRepairButtonState(true);
                return;
            }

            try
            {
                SetStartButtonState(false);
                var progress = new Progress<int>(value => UpdateProgress(value, $"Repairing... ({value}%)"));
                await _repairService.RepairAsync(progress);
                UpdateProgress(100, "Repair complete");
                await Task.Delay(1000);
                SetStartButtonState(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during repair: {ex.Message}");
            }
            finally
            {
                SetRepairButtonState(true);
            }
        }

        private static void OpenUrl(string url) => 
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        #endregion

        #region Game Management

        private void StartGame()
        {
            string[] exeFiles = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, 
                "MicroVolts.exe", SearchOption.AllDirectories);

            if (exeFiles.Length == 0)
            {
                MessageBox.Show(this, "MicroVolts.exe not found.", "Error");
                return;
            }

            string exePath = exeFiles[0];
            SetCompatibilitySettings(exePath);

            try
            {
                var psi = new ProcessStartInfo(exePath) { Verb = "runas" };
                var process = Process.Start(psi);
                if (process != null)
                {
                    MinimizeToTray();
                }
            }
            catch (Exception)
            {
                MessageBox.Show(this, "Failed to start MicroVolts.exe", "Error");
            }
        }

        private async Task InstallGameAsync()
        {
            string installPath = AppDomain.CurrentDomain.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), $"LauncherInstall_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "Full.zip");

            try
            {
                if (!await CheckDiskSpaceAsync(installPath, tempDir)) return;

                var progress = new Progress<int>(value => UpdateProgress(value, $"Installing... ({value}%)"));
                await _downloader.DownloadFileAsync(_config.FullZipUrl, zipPath, progress, 0, 50);

                string extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);

                await Task.Run(() =>
                {
                    try
                    {
                        ZipFile.ExtractToDirectory(zipPath, extractDir);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new Exception("Downloaded file is not a valid ZIP archive. Please check the server configuration.", ex);
                    }
                });

                await CopyExtractedFilesAsync(extractDir, installPath);

                string exePath = Path.Combine(installPath, "MicroVolts.exe");
                SetCompatibilitySettings(exePath);

                UpdateProgress(100, "Installation complete");
                await Task.Delay(1000);

                SetStartButtonState(true);
                if (_autoStart) StartGame();
            }
            catch (Exception ex)
            {
                // Log full exception details
                Logger.Log($"Installation error: {ex.GetType().Name}: {ex.Message}");
                Logger.Log($"Stack trace: {ex.StackTrace}");
                
                // Log all inner exceptions
                var inner = ex.InnerException;
                int depth = 0;
                while (inner != null && depth < 10)
                {
                    Logger.Log($"Inner exception [{depth}]: {inner.GetType().Name}: {inner.Message}");
                    Logger.Log($"Inner stack trace [{depth}]: {inner.StackTrace}");
                    inner = inner.InnerException;
                    depth++;
                }
                
                // Build detailed error message for user
                string errorDetails = $"Error during installation: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetails += $"\n\nInner exception: {ex.InnerException.Message}";
                }
                
                // Add hint for common issues
                if (ex.Message.Contains("decryption", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
                {
                    errorDetails += "\n\nThis appears to be a TLS/SSL connection issue. Please check your internet connection and try again.";
                }
                
                MessageBox.Show(errorDetails, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private async Task<bool> CheckDiskSpaceAsync(string installPath, string tempDir)
        {
            long? zipSize = await _downloader.GetContentLengthAsync(_config.FullZipUrl);
            if (!zipSize.HasValue) return true;

            var installDrive = new DriveInfo(Path.GetPathRoot(installPath)!);
            var tempDrive = new DriveInfo(Path.GetPathRoot(tempDir)!);
            long buffer = 100L * 1024 * 1024;
            long requiredTemp = zipSize.Value + buffer;
            long requiredInstall = zipSize.Value * 2;

            if (tempDrive.AvailableFreeSpace < requiredTemp || installDrive.AvailableFreeSpace < requiredInstall)
            {
                MessageBox.Show("Insufficient disk space for installation. Please free up space and try again.", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private static async Task CopyExtractedFilesAsync(string extractDir, string installPath)
        {
            string[] extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);

            await Parallel.ForEachAsync(extractedFiles, async (file, ct) =>
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("Launcher.exe", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("launcher.txt", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string relativePath = Path.GetRelativePath(extractDir, file);
                string targetPath = Path.Combine(installPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                try
                {
                    await using var source = File.OpenRead(file);
                    await using var dest = File.Create(targetPath);
                    await source.CopyToAsync(dest, ct);
                }
                catch (IOException ex)
                {
                    Logger.Log($"Failed to install file {targetPath}: {ex.Message}");
                }
            });
        }

        #endregion

        #region Utilities

        private void TimeTimer_Tick(object? sender, EventArgs e)
        {
            var cest = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            var cestTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, cest);
            _timeLabel.Text = $"CEST Time: {cestTime:HH:mm:ss}";
        }

        private string GetLauncherChecksum()
        {
            if (_launcherChecksum != null) return _launcherChecksum;

            string exePath = Application.ExecutablePath;
            byte[] data = File.ReadAllBytes(exePath);
            _launcherChecksum = Adler32(data).ToString("x8");
            return _launcherChecksum;
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

        private static void SetCompatibilitySettings(string exePath)
        {
            const string keyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true) ?? 
                           Registry.CurrentUser.CreateSubKey(keyPath);

            if (key == null) return;

            const string expectedValue = "~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE";
            var currentValue = key.GetValue(exePath);

            if (currentValue == null || !currentValue.ToString()!.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(exePath, expectedValue, RegistryValueKind.String);
                Logger.Log("Compatibility settings applied for MicroVolts.exe");
            }
        }

        #endregion
    }
}