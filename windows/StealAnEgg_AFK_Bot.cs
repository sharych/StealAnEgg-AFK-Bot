using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public class MainForm : Form
{
    // ================= P/Invoke Win32 APIs =================
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const uint MOUSEEVENTF_MOVE      = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP   = 0x0010;
    private const uint KEYEVENTF_KEYUP       = 0x0002;

    private const int HOTKEY_ID_F6 = 9001;
    private const int HOTKEY_ID_F7 = 9002;
    private const int HOTKEY_ID_F8 = 9003;
    private const int WM_HOTKEY = 0x0312;
    private const uint VK_F6 = 0x75;
    private const uint VK_F7 = 0x76;
    private const uint VK_F8 = 0x77;

    private enum BotMode
    {
        Idle,
        Fishing,
        Treadmill
    }

    private struct BotSettings
    {
        public double TimeoutSec;
        public int MaxClicks;
        public int ClickSpeedMs;
        public double PostCatchWaitSec;
        public int CastX;
        public int CastY;
        public double CastHoldSec;
        public bool Enable2xGlitch;
        public string RodKey;
        public string SwapKey;
        public bool Randomize;
        public bool EnableAntiAfk;
        public int CamMinSec;
        public int CamMaxSec;
        public int CamDragPx;
        public bool CamArrowAssist;
    }

    // ================= GUI Controls =================
    private Button btnToggleFishing;
    private Button btnToggleTreadmill;
    private Label lblStatus;
    private Label lblFishCount;
    private Label lblElapsedTime;
    private Label lblRadar;
    private TextBox txtLog;

    // Tuning Controls
    private NumericUpDown numCastX;
    private NumericUpDown numCastY;
    private NumericUpDown numCastHold;
    private CheckBox chk2xGlitch;
    private TextBox txtRodKey;
    private TextBox txtSwapKey;

    private NumericUpDown numTimeout;
    private NumericUpDown numPostCatchWait;
    private Button btnTestDetect;
    private Button btnTestClicks;
    private Button btnPickCast;

    private NumericUpDown numClicksCount;
    private NumericUpDown numClickSpeedMs;
    private CheckBox chkRandomize;
    private CheckBox chkAntiAfk;

    // Treadmill & Camera Controls
    private NumericUpDown numCamMinSec;
    private NumericUpDown numCamMaxSec;
    private NumericUpDown numCamDragPx;
    private CheckBox chkCamArrowAssist;
    private Button btnTestCamRotate;

    // ================= Thread-Safe State =================
    private readonly object stateLock = new object();
    private bool _isRunning = false;
    private long _runGeneration = 0;
    private BotMode _activeMode = BotMode.Idle;
    private bool _isTestClicking = false;

    private readonly object settingsLock = new object();
    private BotSettings cachedSettings;
    private bool isLoadingSettings = false;

    private readonly object randLock = new object();
    private Random rand = new Random();

    private Thread botThread = null;
    private DateTime startTime;
    private DateTime lastRadarUiUpdate = DateTime.MinValue;
    private System.Windows.Forms.Timer uiTimer;
    private System.Windows.Forms.Timer radarTimer;
    private int fishCaught = 0;
    private int camRotationsDone = 0;

    // Thread-safe lock for screen capture
    private static readonly object radarLock = new object();
    private static Bitmap radarBmp = new Bitmap(400, 180, PixelFormat.Format24bppRgb);
    private static Graphics radarGraphics = Graphics.FromImage(radarBmp);

    public MainForm()
    {
        InitializeComponents();
        LoadSettingsFromDisk();
        HookSettingsEvents();
        SyncSettingsFromUI(false);
        RegisterGlobalHotkeys();
    }

    private void InitializeComponents()
    {
        this.Text = "Steal an Egg - Smart AFK Fishing & Treadmill Bot (v5.1)";
        this.Size = new Size(680, 760);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(30, 30, 46);
        this.ForeColor = Color.FromArgb(205, 214, 244);
        this.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

        // Header Panel
        Panel pnlHeader = new Panel();
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 135;
        pnlHeader.BackColor = Color.FromArgb(24, 24, 37);
        this.Controls.Add(pnlHeader);

        lblStatus = new Label();
        lblStatus.Text = "STATUS: IDLE (F6 = Fishing Bot | F8 = Treadmill Speed AFK)";
        lblStatus.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        lblStatus.ForeColor = Color.FromArgb(249, 226, 175);
        lblStatus.Location = new Point(20, 12);
        lblStatus.AutoSize = true;
        pnlHeader.Controls.Add(lblStatus);

        btnToggleFishing = new Button();
        btnToggleFishing.Text = "START FISHING (F6)";
        btnToggleFishing.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        btnToggleFishing.BackColor = Color.FromArgb(166, 227, 161);
        btnToggleFishing.ForeColor = Color.FromArgb(17, 17, 27);
        btnToggleFishing.FlatStyle = FlatStyle.Flat;
        btnToggleFishing.FlatAppearance.BorderSize = 0;
        btnToggleFishing.Location = new Point(20, 45);
        btnToggleFishing.Size = new Size(195, 46);
        btnToggleFishing.Cursor = Cursors.Hand;
        btnToggleFishing.Click += delegate { ToggleFishingBot(); };
        pnlHeader.Controls.Add(btnToggleFishing);

        btnToggleTreadmill = new Button();
        btnToggleTreadmill.Text = "TREADMILL AFK (F8)";
        btnToggleTreadmill.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        btnToggleTreadmill.BackColor = Color.FromArgb(137, 180, 250);
        btnToggleTreadmill.ForeColor = Color.FromArgb(17, 17, 27);
        btnToggleTreadmill.FlatStyle = FlatStyle.Flat;
        btnToggleTreadmill.FlatAppearance.BorderSize = 0;
        btnToggleTreadmill.Location = new Point(225, 45);
        btnToggleTreadmill.Size = new Size(200, 46);
        btnToggleTreadmill.Cursor = Cursors.Hand;
        btnToggleTreadmill.Click += delegate { ToggleTreadmillBot(); };
        pnlHeader.Controls.Add(btnToggleTreadmill);

        lblFishCount = new Label();
        lblFishCount.Text = "Fish Caught: 0";
        lblFishCount.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        lblFishCount.ForeColor = Color.FromArgb(137, 220, 235);
        lblFishCount.Location = new Point(445, 45);
        lblFishCount.AutoSize = true;
        pnlHeader.Controls.Add(lblFishCount);

        lblElapsedTime = new Label();
        lblElapsedTime.Text = "Elapsed Time: 00:00:00";
        lblElapsedTime.Font = new Font("Segoe UI", 9.5f);
        lblElapsedTime.ForeColor = Color.FromArgb(166, 173, 200);
        lblElapsedTime.Location = new Point(445, 71);
        lblElapsedTime.AutoSize = true;
        pnlHeader.Controls.Add(lblElapsedTime);

        lblRadar = new Label();
        lblRadar.Text = "Radar Status: Ready (Waiting for STOP button)";
        lblRadar.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblRadar.ForeColor = Color.FromArgb(148, 226, 213);
        lblRadar.Location = new Point(20, 102);
        lblRadar.AutoSize = true;
        pnlHeader.Controls.Add(lblRadar);

        // Tab Control for Settings
        TabControl tabControl = new TabControl();
        tabControl.Location = new Point(15, 145);
        tabControl.Size = new Size(635, 365);
        this.Controls.Add(tabControl);

        TabPage tabDetection = new TabPage("1. Bite Detection & Reeling");
        tabDetection.BackColor = Color.FromArgb(30, 30, 46);
        tabControl.TabPages.Add(tabDetection);

        TabPage tabCasting = new TabPage("2. Casting Settings");
        tabCasting.BackColor = Color.FromArgb(30, 30, 46);
        tabControl.TabPages.Add(tabCasting);

        TabPage tabTreadmill = new TabPage("3. Treadmill & Camera Anti-AFK");
        tabTreadmill.BackColor = Color.FromArgb(30, 30, 46);
        tabControl.TabPages.Add(tabTreadmill);

        // --- TAB 1: DETECTION & REELING SPEED ---
        int y = 20;
        AddLabel(tabDetection, "Bite Timeout (Max wait in water before recasting):", 20, y + 3);
        numTimeout = CreateDecimalNum(tabDetection, 340, y, 3.0m, 35.0m, 9.00m);
        AddLabel(tabDetection, "seconds (Default: 9.00s with 2x Glitch)", 425, y + 3);

        y += 42;
        AddLabel(tabDetection, "Max Click Safety Cap (Stops instantly when caught):", 20, y + 3);
        numClicksCount = CreateNum(tabDetection, 340, y, 15, 80, 48);
        AddLabel(tabDetection, "max clicks", 425, y + 3);

        y += 42;
        AddLabel(tabDetection, "Click Speed (ms delay between taps):", 20, y + 3);
        numClickSpeedMs = CreateNum(tabDetection, 340, y, 5, 80, 30);
        AddLabel(tabDetection, "ms (Default: 30ms)", 425, y + 3);

        y += 42;
        AddLabel(tabDetection, "Post-Catch Wait (Wait for reward popup to clear):", 20, y + 3);
        numPostCatchWait = CreateDecimalNum(tabDetection, 340, y, 1.0m, 5.0m, 2.50m);
        AddLabel(tabDetection, "seconds", 425, y + 3);

        y += 48;
        btnTestClicks = new Button();
        btnTestClicks.Text = "Instant Test 20 Clicks (F7)";
        btnTestClicks.Location = new Point(20, y);
        btnTestClicks.Size = new Size(250, 34);
        btnTestClicks.BackColor = Color.FromArgb(249, 226, 175);
        btnTestClicks.ForeColor = Color.FromArgb(17, 17, 27);
        btnTestClicks.FlatStyle = FlatStyle.Flat;
        btnTestClicks.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        btnTestClicks.Click += delegate { DoTurboClickTest(); };
        tabDetection.Controls.Add(btnTestClicks);

        btnTestDetect = new Button();
        btnTestDetect.Text = "Test Radar Scan Right Now";
        btnTestDetect.Location = new Point(290, y);
        btnTestDetect.Size = new Size(230, 34);
        btnTestDetect.BackColor = Color.FromArgb(137, 180, 250);
        btnTestDetect.ForeColor = Color.FromArgb(17, 17, 27);
        btnTestDetect.FlatStyle = FlatStyle.Flat;
        btnTestDetect.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        btnTestDetect.Click += delegate { TestRadarNow(); };
        tabDetection.Controls.Add(btnTestDetect);

        y += 48;
        Label lblSyncNote = new Label();
        lblSyncNote.Text = "v5.1 Preset Defaults Locked In (Auto-Saved):\n- Timeout: 9.00s | Max Clicks: 48 | Click Speed: 30ms | Post-Catch: 2.50s | 2x Glitch: ON (9 & 3)\n- Press F6 for AFK Fishing, or stand on Treadmill and press F8 for Treadmill Camera Anti-AFK!";
        lblSyncNote.ForeColor = Color.FromArgb(166, 227, 161);
        lblSyncNote.Location = new Point(20, y);
        lblSyncNote.Size = new Size(585, 65);
        tabDetection.Controls.Add(lblSyncNote);

        // --- TAB 2: CASTING ---
        y = 20;
        AddLabel(tabCasting, "Cast Aim Coordinates (Water target):", 20, y);
        y += 25;
        AddLabel(tabCasting, "X:", 20, y + 3);
        numCastX = CreateNum(tabCasting, 45, y, -10000, 10000, 1138);
        AddLabel(tabCasting, "Y:", 140, y + 3);
        numCastY = CreateNum(tabCasting, 165, y, -10000, 10000, 362);

        btnPickCast = new Button();
        btnPickCast.Text = "Pick Point (3s Countdown)";
        btnPickCast.Location = new Point(265, y);
        btnPickCast.Size = new Size(180, 26);
        btnPickCast.BackColor = Color.FromArgb(49, 50, 68);
        btnPickCast.ForeColor = Color.White;
        btnPickCast.FlatStyle = FlatStyle.Flat;
        btnPickCast.Click += delegate { PickCoordinate(numCastX, numCastY, "Cast Target"); };
        tabCasting.Controls.Add(btnPickCast);

        y += 40;
        AddLabel(tabCasting, "Cast Hold Duration (1.10s = PERFECT! Charge):", 20, y + 3);
        numCastHold = CreateDecimalNum(tabCasting, 340, y, 0.5m, 3.0m, 1.10m);

        y += 40;
        chk2xGlitch = new CheckBox();
        chk2xGlitch.Text = "Enable Hotbar Key Swap Glitch (Default ON: Keys 9 & 3)";
        chk2xGlitch.Checked = true;
        chk2xGlitch.Location = new Point(20, y);
        chk2xGlitch.AutoSize = true;
        tabCasting.Controls.Add(chk2xGlitch);

        y += 35;
        AddLabel(tabCasting, "Rod Hotbar Key:", 20, y + 3);
        txtRodKey = new TextBox();
        txtRodKey.Text = "9";
        txtRodKey.Location = new Point(140, y);
        txtRodKey.Size = new Size(40, 24);
        tabCasting.Controls.Add(txtRodKey);

        AddLabel(tabCasting, "Swap Item Key:", 210, y + 3);
        txtSwapKey = new TextBox();
        txtSwapKey.Text = "3";
        txtSwapKey.Location = new Point(320, y);
        txtSwapKey.Size = new Size(40, 24);
        tabCasting.Controls.Add(txtSwapKey);

        // --- TAB 3: TREADMILL & CAMERA ANTI-AFK ---
        y = 18;
        chkRandomize = new CheckBox();
        chkRandomize.Text = "Humanize Timings (+/- random variance to avoid pattern detection)";
        chkRandomize.Checked = true;
        chkRandomize.Location = new Point(20, y);
        chkRandomize.AutoSize = true;
        tabTreadmill.Controls.Add(chkRandomize);

        y += 32;
        chkAntiAfk = new CheckBox();
        chkAntiAfk.Text = "Enable Smooth Right-Click Camera Rotate Anti-AFK (Never presses WASD / never steps off!)";
        chkAntiAfk.Checked = true;
        chkAntiAfk.Location = new Point(20, y);
        chkAntiAfk.AutoSize = true;
        tabTreadmill.Controls.Add(chkAntiAfk);

        y += 38;
        AddLabel(tabTreadmill, "Random Camera Rotate Interval (Min / Max Sec):", 20, y + 3);
        numCamMinSec = CreateNum(tabTreadmill, 330, y, 10, 300, 45);
        AddLabel(tabTreadmill, "to", 412, y + 3);
        numCamMaxSec = CreateNum(tabTreadmill, 435, y, 15, 600, 110);
        AddLabel(tabTreadmill, "sec (1-2m)", 518, y + 3);

        y += 40;
        AddLabel(tabTreadmill, "Camera Right-Click Drag Distance (Pixels):", 20, y + 3);
        numCamDragPx = CreateNum(tabTreadmill, 330, y, 30, 400, 120);
        AddLabel(tabTreadmill, "px (Swings out & returns to center)", 415, y + 3);

        y += 36;
        chkCamArrowAssist = new CheckBox();
        chkCamArrowAssist.Text = "Also gently tap Camera Left/Right Arrow keys (< / >) during rotation for extra anti-idle safety";
        chkCamArrowAssist.Checked = true;
        chkCamArrowAssist.Location = new Point(20, y);
        chkCamArrowAssist.AutoSize = true;
        tabTreadmill.Controls.Add(chkCamArrowAssist);

        y += 38;
        btnTestCamRotate = new Button();
        btnTestCamRotate.Text = "Test Smooth Camera Rotate (2s Delay)";
        btnTestCamRotate.Location = new Point(20, y);
        btnTestCamRotate.Size = new Size(280, 34);
        btnTestCamRotate.BackColor = Color.FromArgb(137, 180, 250);
        btnTestCamRotate.ForeColor = Color.FromArgb(17, 17, 27);
        btnTestCamRotate.FlatStyle = FlatStyle.Flat;
        btnTestCamRotate.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        btnTestCamRotate.Click += delegate {
            Log("Switch to Roblox! Testing smooth camera rotation in 2 seconds...");
            SyncSettingsFromUI(true);
            BotSettings snap = GetCurrentSettings();
            Thread t = new Thread(delegate() {
                Thread.Sleep(2000);
                PerformSmoothCameraRotate(snap, null);
            });
            t.IsBackground = true;
            t.Start();
        };
        tabTreadmill.Controls.Add(btnTestCamRotate);

        y += 44;
        Label lblTreadmillNote = new Label();
        lblTreadmillNote.Text = "How to use Treadmill Speed AFK (F8):\n1. Stand on your treadmill in Roblox so your character is running.\n2. Press F8 (or click 'TREADMILL AFK'). Every random 45-110s, the bot holds Right-Click, smoothly rotates the camera left/right, and returns to center without touching WASD so you NEVER step off!";
        lblTreadmillNote.ForeColor = Color.FromArgb(148, 226, 213);
        lblTreadmillNote.Location = new Point(20, y);
        lblTreadmillNote.Size = new Size(590, 70);
        tabTreadmill.Controls.Add(lblTreadmillNote);

        // Event Log
        Label lblLogTitle = new Label();
        lblLogTitle.Text = "Activity Log:";
        lblLogTitle.Location = new Point(15, 520);
        lblLogTitle.AutoSize = true;
        this.Controls.Add(lblLogTitle);

        Button btnClearLog = new Button();
        btnClearLog.Text = "Clear Log";
        btnClearLog.Location = new Point(565, 517);
        btnClearLog.Size = new Size(85, 24);
        btnClearLog.BackColor = Color.FromArgb(49, 50, 68);
        btnClearLog.ForeColor = Color.White;
        btnClearLog.FlatStyle = FlatStyle.Flat;
        btnClearLog.Click += delegate { txtLog.Clear(); };
        this.Controls.Add(btnClearLog);

        txtLog = new TextBox();
        txtLog.Multiline = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.ReadOnly = true;
        txtLog.Location = new Point(15, 545);
        txtLog.Size = new Size(635, 160);
        txtLog.BackColor = Color.FromArgb(17, 17, 27);
        txtLog.ForeColor = Color.FromArgb(166, 227, 161);
        txtLog.Font = new Font("Consolas", 8.75f);
        this.Controls.Add(txtLog);

        // UI Timer
        uiTimer = new System.Windows.Forms.Timer();
        uiTimer.Interval = 1000;
        uiTimer.Tick += delegate {
            if (IsBotRunning()) {
                TimeSpan elapsed = DateTime.Now - startTime;
                lblElapsedTime.Text = string.Format("Elapsed Time: {0:D2}:{1:D2}:{2:D2}",
                    (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            }
        };
        uiTimer.Start();

        // Live Radar background preview
        radarTimer = new System.Windows.Forms.Timer();
        radarTimer.Interval = 250;
        radarTimer.Tick += delegate {
            if (GetActiveMode() == BotMode.Fishing) return;
            int redCount = 0;
            int threshold = 40;
            bool isBite = ScanForStopButtonFast(out redCount, out threshold);
            UpdateRadarLabel(isBite, redCount, true);
        };
        radarTimer.Start();

        Log("Bot v5.1 ready! Press F6 for Fishing Bot (with your saved settings), or F8 for Treadmill Camera AFK.");
    }

    private void AddLabel(Control parent, string text, int x, int y)
    {
        Label lbl = new Label();
        lbl.Text = text;
        lbl.Location = new Point(x, y);
        lbl.AutoSize = true;
        parent.Controls.Add(lbl);
    }

    private NumericUpDown CreateNum(Control parent, int x, int y, int min, int max, int val)
    {
        NumericUpDown num = new NumericUpDown();
        num.Location = new Point(x, y);
        num.Size = new Size(75, 24);
        num.Minimum = min;
        num.Maximum = max;
        num.Value = val;
        num.BackColor = Color.FromArgb(49, 50, 68);
        num.ForeColor = Color.White;
        parent.Controls.Add(num);
        return num;
    }

    private NumericUpDown CreateDecimalNum(Control parent, int x, int y, decimal min, decimal max, decimal val)
    {
        NumericUpDown num = new NumericUpDown();
        num.Location = new Point(x, y);
        num.Size = new Size(75, 24);
        num.DecimalPlaces = 2;
        num.Increment = 0.05m;
        num.Minimum = min;
        num.Maximum = max;
        num.Value = val;
        num.BackColor = Color.FromArgb(49, 50, 68);
        num.ForeColor = Color.White;
        parent.Controls.Add(num);
        return num;
    }

    private void PickCoordinate(NumericUpDown targetX, NumericUpDown targetY, string name)
    {
        Log(string.Format("Move mouse to {0} in Roblox! Picking in 3 seconds...", name));
        System.Windows.Forms.Timer cdTimer = new System.Windows.Forms.Timer();
        cdTimer.Interval = 3000;
        cdTimer.Tick += delegate {
            cdTimer.Stop();
            cdTimer.Dispose();
            Point cur = Cursor.Position;
            decimal clampedX = Math.Max(targetX.Minimum, Math.Min(targetX.Maximum, (decimal)cur.X));
            decimal clampedY = Math.Max(targetY.Minimum, Math.Min(targetY.Maximum, (decimal)cur.Y));
            targetX.Value = clampedX;
            targetY.Value = clampedY;
            SyncSettingsFromUI(true);
            SystemSounds.Beep.Play();
            Log(string.Format("Picked {0}: X={1}, Y={2} (Saved)", name, (int)clampedX, (int)clampedY));
        };
        cdTimer.Start();
    }

    // ================= Settings Persistence & Thread-Safe Access =================
    private static string GetSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(Path.Combine(appData, "StealAnEgg_AFK_Bot"), "settings.cfg");
    }

    private void HookSettingsEvents()
    {
        EventHandler onChanged = delegate {
            if (!isLoadingSettings)
            {
                SyncSettingsFromUI(true);
            }
        };

        numCastX.ValueChanged += onChanged;
        numCastY.ValueChanged += onChanged;
        numCastHold.ValueChanged += onChanged;
        chk2xGlitch.CheckedChanged += onChanged;
        txtRodKey.TextChanged += onChanged;
        txtSwapKey.TextChanged += onChanged;

        numTimeout.ValueChanged += onChanged;
        numClicksCount.ValueChanged += onChanged;
        numClickSpeedMs.ValueChanged += onChanged;
        numPostCatchWait.ValueChanged += onChanged;

        chkRandomize.CheckedChanged += onChanged;
        chkAntiAfk.CheckedChanged += onChanged;
        numCamMinSec.ValueChanged += onChanged;
        numCamMaxSec.ValueChanged += onChanged;
        numCamDragPx.ValueChanged += onChanged;
        chkCamArrowAssist.CheckedChanged += onChanged;
    }

    private BotSettings ReadSettingsFromUI()
    {
        BotSettings s = new BotSettings();
        s.TimeoutSec = (double)numTimeout.Value;
        s.MaxClicks = (int)numClicksCount.Value;
        s.ClickSpeedMs = (int)numClickSpeedMs.Value;
        s.PostCatchWaitSec = (double)numPostCatchWait.Value;
        s.CastX = (int)numCastX.Value;
        s.CastY = (int)numCastY.Value;
        s.CastHoldSec = (double)numCastHold.Value;
        s.Enable2xGlitch = chk2xGlitch.Checked;
        s.RodKey = string.IsNullOrEmpty(txtRodKey.Text) ? "9" : txtRodKey.Text.Trim();
        s.SwapKey = string.IsNullOrEmpty(txtSwapKey.Text) ? "3" : txtSwapKey.Text.Trim();
        s.Randomize = chkRandomize.Checked;
        s.EnableAntiAfk = chkAntiAfk.Checked;
        s.CamMinSec = (int)numCamMinSec.Value;
        s.CamMaxSec = (int)numCamMaxSec.Value;
        s.CamDragPx = (int)numCamDragPx.Value;
        s.CamArrowAssist = chkCamArrowAssist.Checked;
        return s;
    }

    private BotSettings GetCurrentSettings()
    {
        lock (settingsLock)
        {
            return cachedSettings;
        }
    }

    private void SyncSettingsFromUI(bool save)
    {
        BotSettings s = ReadSettingsFromUI();
        lock (settingsLock)
        {
            cachedSettings = s;
        }
        if (save)
        {
            SaveSettingsToDisk(s);
        }
    }

    private static void SetNumClamped(NumericUpDown ctrl, decimal val)
    {
        if (val < ctrl.Minimum) val = ctrl.Minimum;
        if (val > ctrl.Maximum) val = ctrl.Maximum;
        ctrl.Value = val;
    }

    private void SaveSettingsToDisk(BotSettings s)
    {
        try
        {
            string path = GetSettingsFilePath();
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string[] lines = new string[] {
                "timeoutSec=" + s.TimeoutSec.ToString("F2", CultureInfo.InvariantCulture),
                "maxClicks=" + s.MaxClicks.ToString(CultureInfo.InvariantCulture),
                "clickSpeedMs=" + s.ClickSpeedMs.ToString(CultureInfo.InvariantCulture),
                "postCatchWaitSec=" + s.PostCatchWaitSec.ToString("F2", CultureInfo.InvariantCulture),
                "castX=" + s.CastX.ToString(CultureInfo.InvariantCulture),
                "castY=" + s.CastY.ToString(CultureInfo.InvariantCulture),
                "castHoldSec=" + s.CastHoldSec.ToString("F2", CultureInfo.InvariantCulture),
                "enable2xGlitch=" + (s.Enable2xGlitch ? "1" : "0"),
                "rodKey=" + s.RodKey,
                "swapKey=" + s.SwapKey,
                "randomize=" + (s.Randomize ? "1" : "0"),
                "enableAntiAfk=" + (s.EnableAntiAfk ? "1" : "0"),
                "camMinSec=" + s.CamMinSec.ToString(CultureInfo.InvariantCulture),
                "camMaxSec=" + s.CamMaxSec.ToString(CultureInfo.InvariantCulture),
                "camDragPx=" + s.CamDragPx.ToString(CultureInfo.InvariantCulture),
                "camArrowAssist=" + (s.CamArrowAssist ? "1" : "0")
            };
            File.WriteAllLines(path, lines);
        }
        catch
        {
        }
    }

    private void LoadSettingsFromDisk()
    {
        try
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path)) return;

            isLoadingSettings = true;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                decimal decVal;
                int intVal;
                if (key == "timeoutSec" && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decVal))
                    SetNumClamped(numTimeout, decVal);
                else if (key == "maxClicks" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numClicksCount, intVal);
                else if (key == "clickSpeedMs" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numClickSpeedMs, intVal);
                else if (key == "postCatchWaitSec" && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decVal))
                    SetNumClamped(numPostCatchWait, decVal);
                else if (key == "castX" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numCastX, intVal);
                else if (key == "castY" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numCastY, intVal);
                else if (key == "castHoldSec" && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decVal))
                    SetNumClamped(numCastHold, decVal);
                else if (key == "enable2xGlitch")
                    chk2xGlitch.Checked = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (key == "rodKey" && !string.IsNullOrEmpty(val))
                    txtRodKey.Text = val;
                else if (key == "swapKey" && !string.IsNullOrEmpty(val))
                    txtSwapKey.Text = val;
                else if (key == "randomize")
                    chkRandomize.Checked = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (key == "enableAntiAfk")
                    chkAntiAfk.Checked = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (key == "camMinSec" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numCamMinSec, intVal);
                else if (key == "camMaxSec" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numCamMaxSec, intVal);
                else if (key == "camDragPx" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
                    SetNumClamped(numCamDragPx, intVal);
                else if (key == "camArrowAssist")
                    chkCamArrowAssist.Checked = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
        }
        finally
        {
            isLoadingSettings = false;
        }
    }

    private int NextRand(int minInclusive, int maxExclusive)
    {
        lock (randLock)
        {
            return rand.Next(minInclusive, maxExclusive);
        }
    }

    private bool IsBotRunning()
    {
        lock (stateLock)
        {
            return _isRunning;
        }
    }

    private BotMode GetActiveMode()
    {
        lock (stateLock)
        {
            return _activeMode;
        }
    }

    private bool IsActiveRun(long runId)
    {
        lock (stateLock)
        {
            return _isRunning && _runGeneration == runId;
        }
    }

    private bool SleepInterruptibly(int ms, long runId)
    {
        if (ms <= 0) return IsActiveRun(runId);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (IsActiveRun(runId))
        {
            double remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0) return true;
            int slice = remainingMs > 40 ? 40 : (int)Math.Ceiling(remainingMs);
            if (slice > 0) Thread.Sleep(slice);
        }
        return false;
    }

    // ================= Smooth Right-Click Camera Rotation (Safe for Treadmill & Fishing) =================
    private void PerformSmoothCameraRotate(BotSettings settings, long? runId)
    {
        bool rightDown = false;
        try
        {
            if (runId.HasValue && !IsActiveRun(runId.Value)) return;

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int safeX = screen.Width / 2 + NextRand(-40, 41);
            int safeY = (int)(screen.Height * 0.38) + NextRand(-25, 26);

            Cursor.Position = new Point(safeX, safeY);
            if (runId.HasValue)
            {
                if (!SleepInterruptibly(60, runId.Value)) return;
            }
            else
            {
                Thread.Sleep(60);
            }

            int totalPixels = settings.CamDragPx;
            if (settings.Randomize)
            {
                totalPixels += NextRand(-25, 35);
                if (totalPixels < 40) totalPixels = 40;
            }

            // Randomly pick initial direction (Left = -1, Right = +1)
            int dir = (NextRand(0, 2) == 0) ? 1 : -1;
            int steps = 16;
            int stepDx = (totalPixels / steps) * dir;
            int stepDy = NextRand(-1, 2);

            // Hold Right Mouse Button to rotate camera in Roblox
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            rightDown = true;

            if (runId.HasValue)
            {
                if (!SleepInterruptibly(55, runId.Value)) return;
            }
            else
            {
                Thread.Sleep(55);
            }

            // Smoothly drag camera out using relative mouse_event deltas only
            int walkedSteps = 0;
            for (int i = 0; i < steps; i++)
            {
                if (runId.HasValue && !IsActiveRun(runId.Value)) break;
                mouse_event(MOUSEEVENTF_MOVE, stepDx, stepDy, 0, 0);
                walkedSteps++;
                if (runId.HasValue)
                {
                    if (!SleepInterruptibly(14, runId.Value)) break;
                }
                else
                {
                    Thread.Sleep(14);
                }
            }

            // Brief human pause at peak of camera look
            if (!runId.HasValue || IsActiveRun(runId.Value))
            {
                int pauseMs = NextRand(120, 260);
                if (runId.HasValue) SleepInterruptibly(pauseMs, runId.Value);
                else Thread.Sleep(pauseMs);
            }

            // Smoothly return camera back to original center angle so view never drifts
            for (int i = 0; i < walkedSteps; i++)
            {
                mouse_event(MOUSEEVENTF_MOVE, -stepDx, -stepDy, 0, 0);
                Thread.Sleep(14);
            }

            Thread.Sleep(45);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            rightDown = false;

            if (runId.HasValue && !IsActiveRun(runId.Value)) return;

            // Optional Left/Right arrow camera nudge (rotates Roblox camera without walking/stepping off treadmill)
            if (settings.CamArrowAssist)
            {
                if (runId.HasValue)
                {
                    if (!SleepInterruptibly(80, runId.Value)) return;
                }
                else
                {
                    Thread.Sleep(80);
                }

                byte firstArrow = (dir > 0) ? (byte)Keys.Right : (byte)Keys.Left;
                byte secondArrow = (dir > 0) ? (byte)Keys.Left : (byte)Keys.Right;
                int arrowHold = NextRand(85, 140);

                SimulateKeyPress(firstArrow, arrowHold);
                if (runId.HasValue)
                {
                    if (!SleepInterruptibly(90, runId.Value)) return;
                }
                else
                {
                    Thread.Sleep(90);
                }
                SimulateKeyPress(secondArrow, arrowHold);
            }

            camRotationsDone++;
            Log(string.Format("Camera Rotate #{0} complete ({1}px smooth right-click sweep & return).",
                camRotationsDone, Math.Abs(stepDx * steps)));
        }
        catch (Exception ex)
        {
            Log("Camera rotate error: " + ex.Message);
        }
        finally
        {
            if (rightDown)
            {
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            }
        }
    }

    private int GetNextCameraIntervalSec(BotSettings settings)
    {
        int minS = settings.CamMinSec;
        int maxS = settings.CamMaxSec;
        if (maxS < minS) maxS = minS;
        return NextRand(minS, maxS + 1);
    }

    // ================= Instant Test Clicks (Zero Delay) =================
    private void DoTurboClickTest()
    {
        lock (stateLock)
        {
            if (_isTestClicking) return;
            _isTestClicking = true;
        }

        Log("Sending 20 TURBO clicks instantly right now!");
        Thread t = new Thread(delegate() {
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(10);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    Thread.Sleep(18);
                }
                SystemSounds.Asterisk.Play();
                Log("20 Turbo clicks finished!");
            }
            finally
            {
                lock (stateLock)
                {
                    _isTestClicking = false;
                }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    private void TestRadarNow()
    {
        int redCount = 0;
        int threshold = 40;
        bool detected = ScanForStopButtonFast(out redCount, out threshold);
        string msg = string.Format("Radar Scan:\nSTOP Button Detected: {0}\nRed Pixels: {1} (Threshold: {2})\nScreen: {3}x{4}",
            detected ? "YES! (Minigame Active)" : "NO (Waiting / Normal Gameplay)",
            redCount,
            threshold,
            Screen.PrimaryScreen.Bounds.Width,
            Screen.PrimaryScreen.Bounds.Height);
        Log(msg.Replace("\n", " | "));
        MessageBox.Show(msg, "Radar Test", MessageBoxButtons.OK, detected ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void UpdateRadarLabel(bool isBite, int redCount, bool force)
    {
        DateTime now = DateTime.UtcNow;
        if (!force && (now - lastRadarUiUpdate).TotalMilliseconds < 250) return;
        lastRadarUiUpdate = now;

        InvokeAction(delegate {
            if (isBite)
            {
                lblRadar.Text = string.Format("Radar Status: [MINIGAME STOP BUTTON DETECTED! ({0} red px)]", redCount);
                lblRadar.ForeColor = Color.FromArgb(166, 227, 161);
            }
            else
            {
                lblRadar.Text = string.Format("Radar Status: Waiting for bite ({0} red px in scan zone)", redCount);
                lblRadar.ForeColor = Color.FromArgb(148, 226, 213);
            }
        });
    }

    // ================= Thread-Safe High-Precision STOP Button Radar =================
    private static bool ScanForStopButtonFast(out int redPixelCount, out int threshold)
    {
        redPixelCount = 0;
        threshold = 40;
        lock (radarLock)
        {
            try
            {
                if (radarBmp == null || radarGraphics == null) return false;
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                int minX = (int)(screen.Width * 0.65);
                int maxX = (int)(screen.Width * 0.85);
                int minY = (int)(screen.Height * 0.80);
                int maxY = (int)(screen.Height * 0.95);

                int width = maxX - minX;
                int height = maxY - minY;
                if (width <= 0 || height <= 0) return false;

                int sampledPixels = Math.Max(1, ((width + 2) / 3) * ((height + 2) / 3));
                threshold = Math.Min(50, Math.Max(18, (int)Math.Round(sampledPixels * (40.0 / 6912.0))));

                if (radarBmp.Width != width || radarBmp.Height != height)
                {
                    radarGraphics.Dispose();
                    radarBmp.Dispose();
                    radarBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    radarGraphics = Graphics.FromImage(radarBmp);
                }

                radarGraphics.CopyFromScreen(minX, minY, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

                BitmapData data = radarBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int bytes = Math.Abs(data.Stride) * height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(data.Scan0, rgbValues, 0, bytes);
                radarBmp.UnlockBits(data);

                int stride = data.Stride;
                for (int y = 0; y < height; y += 3)
                {
                    int rowIdx = y * stride;
                    for (int x = 0; x < width; x += 3)
                    {
                        int idx = rowIdx + (x * 3);
                        int b = rgbValues[idx];
                        int g = rgbValues[idx + 1];
                        int r = rgbValues[idx + 2];

                        if (r > 220 && g > 85 && g < 145 && b > 75 && b < 140)
                        {
                            redPixelCount++;
                        }
                    }
                }
                return (redPixelCount >= threshold);
            }
            catch
            {
                return false;
            }
        }
    }

    // ================= Mode Toggles =================
    private void ToggleFishingBot()
    {
        if (IsBotRunning())
        {
            StopAllBots();
        }
        else
        {
            StartFishingBot();
        }
    }

    private void ToggleTreadmillBot()
    {
        if (IsBotRunning())
        {
            StopAllBots();
        }
        else
        {
            StartTreadmillBot();
        }
    }

    private long BeginRun(BotMode mode)
    {
        lock (stateLock)
        {
            if (_isRunning) return 0;
            _isRunning = true;
            _activeMode = mode;
            _runGeneration++;
            return _runGeneration;
        }
    }

    private void StartFishingBot()
    {
        SyncSettingsFromUI(true);
        long runId = BeginRun(BotMode.Fishing);
        if (runId == 0) return;

        startTime = DateTime.Now;

        btnToggleFishing.Text = "STOP FISHING (F6)";
        btnToggleFishing.BackColor = Color.FromArgb(243, 139, 168);
        btnToggleTreadmill.Enabled = false;
        lblFishCount.Text = string.Format("Fish Caught: {0}", fishCaught);
        lblStatus.Text = "STATUS: FISHING RUNNING (Press F6 to Stop)";
        lblStatus.ForeColor = Color.FromArgb(166, 227, 161);

        Log("Fishing Bot STARTED! Casting in 1.5s...");
        SystemSounds.Asterisk.Play();

        botThread = new Thread(delegate() { FishingBotLoop(runId); });
        botThread.IsBackground = true;
        botThread.Start();
    }

    private void StartTreadmillBot()
    {
        SyncSettingsFromUI(true);
        long runId = BeginRun(BotMode.Treadmill);
        if (runId == 0) return;

        startTime = DateTime.Now;

        btnToggleTreadmill.Text = "STOP TREADMILL (F8)";
        btnToggleTreadmill.BackColor = Color.FromArgb(243, 139, 168);
        btnToggleFishing.Enabled = false;
        lblFishCount.Text = string.Format("Cam Rotates: {0}", camRotationsDone);
        lblStatus.Text = "STATUS: TREADMILL AFK RUNNING (Press F8 to Stop)";
        lblStatus.ForeColor = Color.FromArgb(137, 180, 250);

        Log("Treadmill Speed AFK Mode STARTED! Make sure your character is running on the treadmill.");
        SystemSounds.Asterisk.Play();

        botThread = new Thread(delegate() { TreadmillBotLoop(runId); });
        botThread.IsBackground = true;
        botThread.Start();
    }

    private void StopAllBots()
    {
        lock (stateLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            _activeMode = BotMode.Idle;
            _runGeneration++;
        }

        btnToggleFishing.Text = "START FISHING (F6)";
        btnToggleFishing.BackColor = Color.FromArgb(166, 227, 161);
        btnToggleFishing.Enabled = true;

        btnToggleTreadmill.Text = "TREADMILL AFK (F8)";
        btnToggleTreadmill.BackColor = Color.FromArgb(137, 180, 250);
        btnToggleTreadmill.Enabled = true;

        lblStatus.Text = "STATUS: STOPPED (F6 = Fishing | F8 = Treadmill)";
        lblStatus.ForeColor = Color.FromArgb(249, 226, 175);

        Log("Bot STOPPED.");
        SystemSounds.Exclamation.Play();
    }

    // ================= Dedicated Treadmill Speed AFK Loop =================
    private void TreadmillBotLoop(long runId)
    {
        if (!SleepInterruptibly(2000, runId)) return;

        // Perform an initial gentle camera check so the user immediately sees it working
        if (IsActiveRun(runId))
        {
            InvokeAction(delegate {
                lblStatus.Text = "STATUS: Treadmill Mode - Rotating Camera...";
                lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
            });
            PerformSmoothCameraRotate(GetCurrentSettings(), runId);
            InvokeAction(delegate {
                lblFishCount.Text = string.Format("Cam Rotates: {0}", camRotationsDone);
            });
        }

        while (IsActiveRun(runId))
        {
            BotSettings settings = GetCurrentSettings();
            int waitSec = GetNextCameraIntervalSec(settings);
            Log(string.Format("Staying on treadmill (zero WASD). Next random camera rotate in {0} seconds...", waitSec));

            DateTime nextRotate = DateTime.Now.AddSeconds(waitSec);
            while (IsActiveRun(runId) && DateTime.Now < nextRotate)
            {
                int remaining = Math.Max(0, (int)Math.Ceiling((nextRotate - DateTime.Now).TotalSeconds));
                InvokeAction(delegate {
                    lblStatus.Text = string.Format("STATUS: TREADMILL AFK (Next Camera Rotate in {0}s)", remaining);
                    lblStatus.ForeColor = Color.FromArgb(137, 180, 250);
                });
                SleepInterruptibly(500, runId);
            }

            if (!IsActiveRun(runId)) break;

            InvokeAction(delegate {
                lblStatus.Text = "STATUS: Treadmill Mode - Rotating Camera...";
                lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
            });
            PerformSmoothCameraRotate(GetCurrentSettings(), runId);
            InvokeAction(delegate {
                lblFishCount.Text = string.Format("Cam Rotates: {0}", camRotationsDone);
            });
        }
    }

    // ================= Fishing Bot Loop =================
    private void FishingBotLoop(long runId)
    {
        if (!SleepInterruptibly(1500, runId)) return;

        DateTime nextCameraAfk = DateTime.Now.AddSeconds(GetNextCameraIntervalSec(GetCurrentSettings()));

        while (IsActiveRun(runId))
        {
            try
            {
                BotSettings settings = GetCurrentSettings();

                // 0. If a minigame is ALREADY on screen right now, reel it in first!
                int initialRed;
                int threshold;
                bool initialDetected = ScanForStopButtonFast(out initialRed, out threshold);
                UpdateRadarLabel(initialDetected, initialRed, false);
                if (initialDetected)
                {
                    ReelInFish(settings, runId);
                    continue;
                }

                // 1. Smooth Camera Rotate Anti-AFK Check (only while rod is uncast between casts)
                if (settings.EnableAntiAfk && DateTime.Now >= nextCameraAfk)
                {
                    InvokeAction(delegate {
                        lblStatus.Text = "STATUS: Anti-AFK Smooth Camera Rotate...";
                        lblStatus.ForeColor = Color.FromArgb(137, 220, 235);
                    });
                    PerformSmoothCameraRotate(settings, runId);
                    nextCameraAfk = DateTime.Now.AddSeconds(GetNextCameraIntervalSec(settings));
                    if (!SleepInterruptibly(350, runId)) break;
                }

                if (!IsActiveRun(runId)) break;

                // 2. Casting
                double castHoldSec = settings.CastHoldSec;
                InvokeAction(delegate {
                    lblStatus.Text = string.Format("STATUS: Charging Cast ({0:F2}s)...", castHoldSec);
                    lblStatus.ForeColor = Color.FromArgb(205, 214, 244);
                });

                if (settings.Enable2xGlitch)
                {
                    byte rodKey = GetVirtualKey(settings.RodKey, (byte)Keys.D9);
                    byte swapKey = GetVirtualKey(settings.SwapKey, (byte)Keys.D3);

                    SimulateKeyPress(rodKey, 50);
                    if (!SleepInterruptibly(150, runId)) break;
                    SimulateKeyPress(swapKey, 50);
                    if (!SleepInterruptibly(1450, runId)) break;
                }

                if (!IsActiveRun(runId)) break;

                int targetX = settings.CastX;
                int targetY = settings.CastY;
                if (settings.Randomize)
                {
                    targetX += NextRand(-3, 4);
                    targetY += NextRand(-3, 4);
                }

                Cursor.Position = new Point(targetX, targetY);
                if (!SleepInterruptibly(80, runId)) break;

                // Hold left-click to hit PERFECT! cast (guarantee LEFTUP even if stopped mid-hold)
                int holdMs = (int)(settings.CastHoldSec * 1000.0);
                if (settings.Randomize)
                {
                    holdMs += NextRand(-25, 25);
                    if (holdMs < 150) holdMs = 150;
                }

                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                bool holdCompleted = SleepInterruptibly(holdMs, runId);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                if (!holdCompleted) break;

                Log(string.Format("Casted rod ({0}ms hold). Waiting up to {1:F2}s for bite...", holdMs, settings.TimeoutSec));

                InvokeAction(delegate {
                    lblStatus.Text = "STATUS: Line in Water - Waiting for Bite...";
                    lblStatus.ForeColor = Color.FromArgb(249, 226, 175);
                });

                // Wait 1.0s for bobber to land in water
                if (!SleepInterruptibly(1000, runId)) break;

                // 3. Bite Waiting Loop
                DateTime waitStart = DateTime.Now;
                double maxWaitSec = settings.TimeoutSec;
                bool biteDetected = false;

                while (IsActiveRun(runId) && (DateTime.Now - waitStart).TotalSeconds < maxWaitSec)
                {
                    int redCount;
                    int th;
                    bool hit1 = ScanForStopButtonFast(out redCount, out th);
                    UpdateRadarLabel(hit1, redCount, false);
                    if (hit1)
                    {
                        if (!SleepInterruptibly(25, runId)) break;
                        bool hit2 = ScanForStopButtonFast(out redCount, out th);
                        UpdateRadarLabel(hit2, redCount, false);
                        if (hit2)
                        {
                            biteDetected = true;
                            break;
                        }
                    }
                    if (!SleepInterruptibly(25, runId)) break;
                }

                if (!IsActiveRun(runId)) break;

                // 4. Hook & Reel OR Clean Timeout Reset
                if (biteDetected)
                {
                    ReelInFish(settings, runId);
                }
                else
                {
                    Log("No bite after timeout. Pulling bobber out of water to reset...");
                    InvokeAction(delegate {
                        lblStatus.Text = "STATUS: Resetting Line...";
                        lblStatus.ForeColor = Color.FromArgb(243, 139, 168);
                    });

                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                    SleepInterruptibly(1300, runId);
                }
            }
            catch (Exception ex)
            {
                Log("Loop error: " + ex.Message);
                SleepInterruptibly(1000, runId);
            }
        }
    }

    private void ReelInFish(BotSettings settings, long runId)
    {
        InvokeAction(delegate {
            lblStatus.Text = "STATUS: BITE DETECTED! TURBO REELING!";
            lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
        });

        int clickSpeed = settings.ClickSpeedMs;
        int maxCap = settings.MaxClicks;
        int clicksSent = 0;

        Log("Bite confirmed! Turbo clicking while STOP button is active...");

        while (IsActiveRun(runId) && clicksSent < maxCap)
        {
            if (clicksSent >= 4)
            {
                int redCount;
                int th;
                bool hit1 = ScanForStopButtonFast(out redCount, out th);
                UpdateRadarLabel(hit1, redCount, false);
                if (!hit1)
                {
                    if (!SleepInterruptibly(25, runId)) break;
                    bool hit2 = ScanForStopButtonFast(out redCount, out th);
                    UpdateRadarLabel(hit2, redCount, false);
                    if (!hit2)
                    {
                        break;
                    }
                }
            }

            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(10);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            clicksSent++;
            if (!SleepInterruptibly(clickSpeed, runId)) break;
        }

        if (!IsActiveRun(runId)) return;

        fishCaught++;
        InvokeAction(delegate {
            lblFishCount.Text = string.Format("Fish Caught: {0}", fishCaught);
            lblStatus.Text = "STATUS: Fish Caught! Waiting for reward popup...";
            lblStatus.ForeColor = Color.FromArgb(137, 220, 235);
        });

        int postWaitMs = (int)(settings.PostCatchWaitSec * 1000.0);
        Log(string.Format("Caught Fish #{0} in {1} clicks! Waiting {2}ms for popup to finish...", fishCaught, clicksSent, postWaitMs));

        SleepInterruptibly(postWaitMs, runId);
    }

    private void SimulateKeyPress(byte vk, int holdMs)
    {
        keybd_event(vk, 0, 0, 0);
        Thread.Sleep(holdMs);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    private byte GetVirtualKey(string text, byte fallback)
    {
        if (string.IsNullOrEmpty(text)) return fallback;
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return fallback;
        char c = char.ToUpper(trimmed[0]);
        if (c >= '0' && c <= '9') return (byte)(0x30 + (c - '0'));
        if (c >= 'A' && c <= 'Z') return (byte)(0x41 + (c - 'A'));
        return fallback;
    }

    private void InvokeAction(Action act)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        try
        {
            this.BeginInvoke(act);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Log(string msg)
    {
        string entry = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, msg);
        InvokeAction(delegate {
            if (txtLog.TextLength > 10000) txtLog.Clear();
            txtLog.AppendText(entry + Environment.NewLine);
        });
    }

    // ================= Global Hotkeys =================
    private void RegisterGlobalHotkeys()
    {
        if (!RegisterHotKey(this.Handle, HOTKEY_ID_F6, 0, VK_F6))
            Log("Warning: Could not register global hotkey F6 (already in use by another app).");
        if (!RegisterHotKey(this.Handle, HOTKEY_ID_F7, 0, VK_F7))
            Log("Warning: Could not register global hotkey F7 (already in use by another app).");
        if (!RegisterHotKey(this.Handle, HOTKEY_ID_F8, 0, VK_F8))
            Log("Warning: Could not register global hotkey F8 (already in use by another app).");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HOTKEY_ID_F6)
            {
                ToggleFishingBot();
            }
            else if (id == HOTKEY_ID_F7)
            {
                DoTurboClickTest();
            }
            else if (id == HOTKEY_ID_F8)
            {
                ToggleTreadmillBot();
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SyncSettingsFromUI(true);
        lock (stateLock)
        {
            _isRunning = false;
            _activeMode = BotMode.Idle;
            _runGeneration++;
        }

        if (uiTimer != null) uiTimer.Stop();
        if (radarTimer != null) radarTimer.Stop();

        UnregisterHotKey(this.Handle, HOTKEY_ID_F6);
        UnregisterHotKey(this.Handle, HOTKEY_ID_F7);
        UnregisterHotKey(this.Handle, HOTKEY_ID_F8);

        lock (radarLock)
        {
            if (radarGraphics != null) { radarGraphics.Dispose(); radarGraphics = null; }
            if (radarBmp != null) { radarBmp.Dispose(); radarBmp = null; }
        }

        base.OnFormClosing(e);
    }

    [STAThread]
    public static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
