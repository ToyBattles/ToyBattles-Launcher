namespace Launcher
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            bgPicture = new PictureBox();
            startButton = new PictureBox();
            progressBar1 = new TextProgressBar();
            exitButton = new PictureBox();
            DiscordButton = new PictureBox();
            WebsiteButton = new PictureBox();
            RepairButton = new PictureBox();
            launcherversion = new Label();
            trayIcon = new NotifyIcon(components);
            trayContextMenu = new ContextMenuStrip(components);
            showMenuItem = new ToolStripMenuItem();
            runInBackgroundMenuItem = new ToolStripMenuItem();
            stopLauncherMenuItem = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)bgPicture).BeginInit();
            ((System.ComponentModel.ISupportInitialize)startButton).BeginInit();
            ((System.ComponentModel.ISupportInitialize)exitButton).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DiscordButton).BeginInit();
            ((System.ComponentModel.ISupportInitialize)WebsiteButton).BeginInit();
            ((System.ComponentModel.ISupportInitialize)RepairButton).BeginInit();
            trayContextMenu.SuspendLayout();
            SuspendLayout();
            // 
            // bgPicture
            // 
            bgPicture.BackColor = Color.Transparent;
            bgPicture.BackgroundImageLayout = ImageLayout.None;
            bgPicture.Image = Properties.Resources.Launcher_BG;
            bgPicture.Location = new Point(2, -1);
            bgPicture.Name = "bgPicture";
            bgPicture.Size = new Size(1068, 587);
            bgPicture.SizeMode = PictureBoxSizeMode.CenterImage;
            bgPicture.TabIndex = 1;
            bgPicture.TabStop = false;
            // 
            // startButton
            // 
            startButton.BackgroundImage = Properties.Resources.startDisabled;
            startButton.BackgroundImageLayout = ImageLayout.Stretch;
            startButton.ErrorImage = Properties.Resources.startDisabled;
            startButton.InitialImage = Properties.Resources.startDisabled;
            startButton.Location = new Point(372, 473);
            startButton.Name = "startButton";
            startButton.Size = new Size(350, 85);
            startButton.TabIndex = 2;
            startButton.TabStop = false;
            startButton.WaitOnLoad = true;
            // 
            // progressBar1
            // 
            progressBar1.BackColor = Color.Gray;
            progressBar1.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            progressBar1.ForeColor = Color.Black;
            progressBar1.Location = new Point(153, 426);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(768, 15);
            progressBar1.Step = 1;
            progressBar1.TabIndex = 4;
            // 
            // exitButton
            // 
            exitButton.BackgroundImage = Properties.Resources.exitNormal;
            exitButton.BackgroundImageLayout = ImageLayout.Stretch;
            exitButton.ErrorImage = Properties.Resources.exitDisabled;
            exitButton.InitialImage = Properties.Resources.exitDisabled;
            exitButton.Location = new Point(975, 48);
            exitButton.Name = "exitButton";
            exitButton.Size = new Size(29, 28);
            exitButton.TabIndex = 5;
            exitButton.TabStop = false;
            exitButton.WaitOnLoad = true;
            //
            // DiscordButton
            //
            DiscordButton.BackgroundImage = Properties.Resources.discordNormal;
            DiscordButton.BackgroundImageLayout = ImageLayout.Zoom;
            DiscordButton.ErrorImage = Properties.Resources.discordDisabled;
            DiscordButton.InitialImage = Properties.Resources.discordDisabled;
            DiscordButton.Location = new Point(932, 462);
            DiscordButton.Name = "DiscordButton";
            DiscordButton.Size = new Size(47, 36);
            DiscordButton.TabIndex = 6;
            DiscordButton.TabStop = false;
            DiscordButton.WaitOnLoad = true;
            //
            // WebsiteButton
            //
            WebsiteButton.BackgroundImage = Properties.Resources.webNormal;
            WebsiteButton.BackgroundImageLayout = ImageLayout.Stretch;
            WebsiteButton.ErrorImage = Properties.Resources.webDisabled;
            WebsiteButton.InitialImage = Properties.Resources.webNormal;
            WebsiteButton.Location = new Point(885, 462);
            WebsiteButton.Name = "WebsiteButton";
            WebsiteButton.Size = new Size(41, 36);
            WebsiteButton.TabIndex = 7;
            WebsiteButton.TabStop = false;
            WebsiteButton.WaitOnLoad = true;
            //
            // RepairButton
            //
            RepairButton.BackgroundImage = Properties.Resources.repairDisabled;
            RepairButton.BackgroundImageLayout = ImageLayout.Stretch;
            RepairButton.ErrorImage = Properties.Resources.repairDisabled;
            RepairButton.InitialImage = Properties.Resources.repairDisabled;
            RepairButton.Location = new Point(838, 462);
            RepairButton.Name = "RepairButton";
            RepairButton.Size = new Size(41, 36);
            RepairButton.TabIndex = 8;
            RepairButton.TabStop = false;
            RepairButton.WaitOnLoad = true;
            //
            // launcherversion
            //
            launcherversion.Location = new Point(0, 0);
            launcherversion.Name = "launcherversion";
            launcherversion.Size = new Size(100, 23);
            launcherversion.TabIndex = 0;
            //
            // trayIcon
            //
            trayIcon.ContextMenuStrip = trayContextMenu;
            trayIcon.Icon = (Icon)resources.GetObject("$this.Icon");
            trayIcon.Text = "ToyBattles Launcher";
            trayIcon.Visible = false;
            trayIcon.DoubleClick += TrayIcon_DoubleClick;
            //
            // trayContextMenu
            //
            trayContextMenu.Items.AddRange(new ToolStripItem[] { showMenuItem, runInBackgroundMenuItem, new ToolStripSeparator(), stopLauncherMenuItem });
            trayContextMenu.Name = "trayContextMenu";
            trayContextMenu.Size = new Size(181, 92);
            //
            // showMenuItem
            //
            showMenuItem.Name = "showMenuItem";
            showMenuItem.Size = new Size(180, 22);
            showMenuItem.Text = "Show Launcher";
            showMenuItem.Font = new Font(showMenuItem.Font, FontStyle.Bold);
            showMenuItem.Click += ShowMenuItem_Click;
            //
            // runInBackgroundMenuItem
            //
            runInBackgroundMenuItem.Name = "runInBackgroundMenuItem";
            runInBackgroundMenuItem.Size = new Size(180, 22);
            runInBackgroundMenuItem.Text = "Run in Background";
            runInBackgroundMenuItem.CheckOnClick = true;
            runInBackgroundMenuItem.Checked = true;
            runInBackgroundMenuItem.Click += RunInBackgroundMenuItem_Click;
            //
            // stopLauncherMenuItem
            //
            stopLauncherMenuItem.Name = "stopLauncherMenuItem";
            stopLauncherMenuItem.Size = new Size(180, 22);
            stopLauncherMenuItem.Text = "Stop Launcher";
            stopLauncherMenuItem.Click += StopLauncherMenuItem_Click;
            //
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Black;
            BackgroundImageLayout = ImageLayout.None;
            ClientSize = new Size(1068, 589);
            ControlBox = false;
            Controls.Add(launcherversion);
            Controls.Add(RepairButton);
            Controls.Add(WebsiteButton);
            Controls.Add(DiscordButton);
            Controls.Add(exitButton);
            Controls.Add(progressBar1);
            Controls.Add(startButton);
            Controls.Add(bgPicture);
            DoubleBuffered = true;
            ForeColor = Color.Transparent;
            FormBorderStyle = FormBorderStyle.None;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            ShowIcon = false;
            Text = "ToyBattles Launcher";
            TransparencyKey = Color.Transparent;
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)bgPicture).EndInit();
            ((System.ComponentModel.ISupportInitialize)startButton).EndInit();
            ((System.ComponentModel.ISupportInitialize)exitButton).EndInit();
            ((System.ComponentModel.ISupportInitialize)DiscordButton).EndInit();
            ((System.ComponentModel.ISupportInitialize)WebsiteButton).EndInit();
            ((System.ComponentModel.ISupportInitialize)RepairButton).EndInit();
            trayContextMenu.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private PictureBox bgPicture;
        private PictureBox startButton;
        private TextProgressBar progressBar1;
        private PictureBox exitButton;
        private PictureBox DiscordButton;
        private PictureBox WebsiteButton;
        private PictureBox RepairButton;
        private Label launcherversion;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayContextMenu;
        private ToolStripMenuItem showMenuItem;
        private ToolStripMenuItem runInBackgroundMenuItem;
        private ToolStripMenuItem stopLauncherMenuItem;
    }
}
