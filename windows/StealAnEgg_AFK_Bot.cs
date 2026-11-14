using System;
using System.Drawing;
using System.Drawing.Imaging;
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
    private volatile bool isRunning = false;
    private Thread botThread = null;
    private DateTime startTime;
    private System.Windows.Forms.Timer uiTimer;
    private System.Windows.Forms.Timer radarTimer;
    private int fishCaught = 0;
    private int camRotationsDone = 0;
    private Random rand = new Random();

    // Thread-safe lock for screen capture
    private static readonly object radarLock = new object();
    private static Bitmap radarBmp = new Bitmap(400, 180, PixelFormat.Format24bppRgb);
    private static Graphics radarGraphics = Graphics.FromImage(radarBmp);

    public MainForm()
    {
        InitializeComponents();
        RegisterGlobalHotkeys();
    }

    private void InitializeComponents()
    {
        this.Text = "Steal an Egg - Smart AFK Fishing & Treadmill Bot (v5.0)";
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
        lblSyncNote.Text = "v5.0 Preset Defaults Locked In:\n- Timeout: 9.00s | Max Clicks: 48 | Click Speed: 30ms | Post-Catch: 2.50s | 2x Glitch: ON (9 & 3)\n- Press F6 for AFK Fishing, or stand on Treadmill and press F8 for Treadmill Camera Anti-AFK!";
        lblSyncNote.ForeColor = Color.FromArgb(166, 227, 161);
        lblSyncNote.Location = new Point(20, y);
        lblSyncNote.Size = new Size(585, 65);
        tabDetection.Controls.Add(lblSyncNote);

        // --- TAB 2: CASTING ---
        y = 20;
        AddLabel(tabCasting, "Cast Aim Coordinates (Water target):", 20, y);
        y += 25;
        AddLabel(tabCasting, "X:", 20, y + 3);
        numCastX = CreateNum(tabCasting, 45, y, 0, 3840, 1138);
        AddLabel(tabCasting, "Y:", 140, y + 3);
        numCastY = CreateNum(tabCasting, 165, y, 0, 2160, 362);

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
            Thread t = new Thread(delegate() {
                Thread.Sleep(2000);
                PerformSmoothCameraRotate();
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
            if (isRunning) {
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
            int redCount = 0;
            bool isBite = ScanForStopButtonFast(out redCount);
            if (isBite) {
                lblRadar.Text = string.Format("Radar Status: [MINIGAME STOP BUTTON DETECTED! ({0} red px)]", redCount);
                lblRadar.ForeColor = Color.FromArgb(166, 227, 161);
            } else {
                lblRadar.Text = string.Format("Radar Status: Waiting for bite ({0} red px in scan zone)", redCount);
                lblRadar.ForeColor = Color.FromArgb(148, 226, 213);
            }
        };
        radarTimer.Start();

        Log("Bot v5.0 ready! Press F6 for Fishing Bot (with your saved defaults), or F8 for Treadmill Camera AFK.");
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
            targetX.Value = cur.X;
            targetY.Value = cur.Y;
            SystemSounds.Beep.Play();
            Log(string.Format("Picked {0}: X={1}, Y={2}", name, cur.X, cur.Y));
        };
        cdTimer.Start();
    }

    // ================= Smooth Right-Click Camera Rotation (Safe for Treadmill & Fishing) =================
    private void PerformSmoothCameraRotate()
    {
        try
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int safeX = screen.Width / 2 + rand.Next(-40, 41);
            int safeY = (int)(screen.Height * 0.38) + rand.Next(-25, 26);

            Cursor.Position = new Point(safeX, safeY);
            Thread.Sleep(60);

            int totalPixels = (int)numCamDragPx.Value;
            if (chkRandomize.Checked)
            {
                totalPixels += rand.Next(-25, 35);
                if (totalPixels < 40) totalPixels = 40;
            }

            // Randomly pick initial direction (Left = -1, Right = +1)
            int dir = (rand.Next(0, 2) == 0) ? 1 : -1;
            int steps = 16;
            int stepDx = (totalPixels / steps) * dir;
            int stepDy = rand.Next(-1, 2);

            // Hold Right Mouse Button to rotate camera in Roblox
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            Thread.Sleep(55);

            // Smoothly drag camera out
            for (int i = 0; i < steps; i++)
            {
                mouse_event(MOUSEEVENTF_MOVE, stepDx, stepDy, 0, 0);
                Cursor.Position = new Point(Cursor.Position.X + stepDx, Cursor.Position.Y + stepDy);
                Thread.Sleep(14);
            }

            // Brief human pause at peak of camera look
            Thread.Sleep(rand.Next(120, 260));

            // Smoothly return camera back to original center angle so view never drifts
            for (int i = 0; i < steps; i++)
            {
                mouse_event(MOUSEEVENTF_MOVE, -stepDx, -stepDy, 0, 0);
                Cursor.Position = new Point(Cursor.Position.X - stepDx, Cursor.Position.Y - stepDy);
                Thread.Sleep(14);
            }

            Thread.Sleep(45);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);

            // Optional Left/Right arrow camera nudge (rotates Roblox camera without walking/stepping off treadmill)
            if (chkCamArrowAssist.Checked)
            {
                Thread.Sleep(80);
                byte firstArrow = (dir > 0) ? (byte)Keys.Right : (byte)Keys.Left;
                byte secondArrow = (dir > 0) ? (byte)Keys.Left : (byte)Keys.Right;
                int arrowHold = rand.Next(85, 140);

                SimulateKeyPress(firstArrow, arrowHold);
                Thread.Sleep(90);
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
    }

    private int GetNextCameraIntervalSec()
    {
        int minS = (int)numCamMinSec.Value;
        int maxS = (int)numCamMaxSec.Value;
        if (maxS < minS) maxS = minS;
        return rand.Next(minS, maxS + 1);
    }

    // ================= Instant Test Clicks (Zero Delay) =================
    private void DoTurboClickTest()
    {
        Log("Sending 20 TURBO clicks instantly right now!");
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

    private void TestRadarNow()
    {
        int redCount = 0;
        bool detected = ScanForStopButtonFast(out redCount);
        string msg = string.Format("Radar Scan:\nSTOP Button Detected: {0}\nRed Pixels: {1} (Threshold: 40)\nScreen: {2}x{3}",
            detected ? "YES! (Minigame Active)" : "NO (Waiting / Normal Gameplay)",
            redCount,
            Screen.PrimaryScreen.Bounds.Width,
            Screen.PrimaryScreen.Bounds.Height);
        Log(msg.Replace("\n", " | "));
        MessageBox.Show(msg, "Radar Test", MessageBoxButtons.OK, detected ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // ================= Thread-Safe High-Precision STOP Button Radar =================
    private static bool ScanForStopButtonFast(out int redPixelCount)
    {
        redPixelCount = 0;
        lock (radarLock)
        {
            try
            {
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                int minX = (int)(screen.Width * 0.65);
                int maxX = (int)(screen.Width * 0.85);
                int minY = (int)(screen.Height * 0.80);
                int maxY = (int)(screen.Height * 0.95);

                int width = maxX - minX;
                int height = maxY - minY;
                if (width <= 0 || height <= 0) return false;

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
                return (redPixelCount >= 40);
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
        if (isRunning)
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
        if (isRunning)
        {
            StopAllBots();
        }
        else
        {
            StartTreadmillBot();
        }
    }

    private void StartFishingBot()
    {
        if (isRunning) return;
        isRunning = true;
        startTime = DateTime.Now;

        btnToggleFishing.Text = "STOP FISHING (F6)";
        btnToggleFishing.BackColor = Color.FromArgb(243, 139, 168);
        btnToggleTreadmill.Enabled = false;
        lblStatus.Text = "STATUS: FISHING RUNNING (Press F6 to Stop)";
        lblStatus.ForeColor = Color.FromArgb(166, 227, 161);

        Log("Fishing Bot STARTED! Casting in 1.5s...");
        SystemSounds.Asterisk.Play();

        botThread = new Thread(FishingBotLoop);
        botThread.IsBackground = true;
        botThread.Start();
    }

    private void StartTreadmillBot()
    {
        if (isRunning) return;
        isRunning = true;
        startTime = DateTime.Now;

        btnToggleTreadmill.Text = "STOP TREADMILL (F8)";
        btnToggleTreadmill.BackColor = Color.FromArgb(243, 139, 168);
        btnToggleFishing.Enabled = false;
        lblStatus.Text = "STATUS: TREADMILL AFK RUNNING (Press F8 to Stop)";
        lblStatus.ForeColor = Color.FromArgb(137, 180, 250);

        Log("Treadmill Speed AFK Mode STARTED! Make sure your character is running on the treadmill.");
        SystemSounds.Asterisk.Play();

        botThread = new Thread(TreadmillBotLoop);
        botThread.IsBackground = true;
        botThread.Start();
    }

    private void StopAllBots()
    {
        if (!isRunning) return;
        isRunning = false;

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
    private void TreadmillBotLoop()
    {
        Thread.Sleep(2000);

        // Perform an initial gentle camera check so the user immediately sees it working
        if (isRunning)
        {
            InvokeAction(delegate {
                lblStatus.Text = "STATUS: Treadmill Mode - Rotating Camera...";
                lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
            });
            PerformSmoothCameraRotate();
        }

        while (isRunning)
        {
            int waitSec = GetNextCameraIntervalSec();
            Log(string.Format("Staying on treadmill (zero WASD). Next random camera rotate in {0} seconds...", waitSec));

            DateTime nextRotate = DateTime.Now.AddSeconds(waitSec);
            while (isRunning && DateTime.Now < nextRotate)
            {
                int remaining = (int)Math.Ceiling((nextRotate - DateTime.Now).TotalSeconds);
                InvokeAction(delegate {
                    lblStatus.Text = string.Format("STATUS: TREADMILL AFK (Next Camera Rotate in {0}s)", remaining);
                    lblStatus.ForeColor = Color.FromArgb(137, 180, 250);
                });
                Thread.Sleep(500);
            }

            if (!isRunning) break;

            InvokeAction(delegate {
                lblStatus.Text = "STATUS: Treadmill Mode - Rotating Camera...";
                lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
            });
            PerformSmoothCameraRotate();
            InvokeAction(delegate {
                lblFishCount.Text = string.Format("Cam Rotates: {0}", camRotationsDone);
            });
        }
    }

    // ================= Fishing Bot Loop =================
    private void FishingBotLoop()
    {
        Thread.Sleep(1500);

        DateTime nextCameraAfk = DateTime.Now.AddSeconds(GetNextCameraIntervalSec());

        while (isRunning)
        {
            try
            {
                // 0. If a minigame is ALREADY on screen right now, reel it in first!
                int initialRed;
                if (ScanForStopButtonFast(out initialRed))
                {
                    ReelInFish();
                    continue;
                }

                // 1. Smooth Camera Rotate Anti-AFK Check (only while rod is uncast between casts)
                if (chkAntiAfk.Checked && DateTime.Now >= nextCameraAfk)
                {
                    InvokeAction(delegate {
                        lblStatus.Text = "STATUS: Anti-AFK Smooth Camera Rotate...";
                        lblStatus.ForeColor = Color.FromArgb(137, 220, 235);
                    });
                    PerformSmoothCameraRotate();
                    nextCameraAfk = DateTime.Now.AddSeconds(GetNextCameraIntervalSec());
                    Thread.Sleep(350);
                }

                if (!isRunning) break;

                // 2. Casting
                InvokeAction(delegate {
                    lblStatus.Text = "STATUS: Charging Cast (1.10s)...";
                    lblStatus.ForeColor = Color.FromArgb(205, 214, 244);
                });

                if (chk2xGlitch.Checked)
                {
                    byte rodKey = GetVirtualKey(txtRodKey.Text, (byte)Keys.D9);
                    byte swapKey = GetVirtualKey(txtSwapKey.Text, (byte)Keys.D3);

                    SimulateKeyPress(rodKey, 50);
                    Thread.Sleep(150);
                    SimulateKeyPress(swapKey, 50);
                    Thread.Sleep(1450);
                }

                if (!isRunning) break;

                int targetX = (int)numCastX.Value;
                int targetY = (int)numCastY.Value;
                if (chkRandomize.Checked)
                {
                    targetX += rand.Next(-3, 4);
                    targetY += rand.Next(-3, 4);
                }

                Cursor.Position = new Point(targetX, targetY);
                Thread.Sleep(80);

                // Hold left-click for 1.10s to hit PERFECT! cast
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                int holdMs = (int)(numCastHold.Value * 1000m);
                if (chkRandomize.Checked)
                {
                    holdMs += rand.Next(-25, 25);
                }
                Thread.Sleep(holdMs);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                Log(string.Format("Casted rod ({0}ms hold). Waiting up to {1}s for bite...", holdMs, numTimeout.Value));

                InvokeAction(delegate {
                    lblStatus.Text = "STATUS: Line in Water - Waiting for Bite...";
                    lblStatus.ForeColor = Color.FromArgb(249, 226, 175);
                });

                // Wait 1.0s for bobber to land in water
                Thread.Sleep(1000);

                // 3. Bite Waiting Loop
                DateTime waitStart = DateTime.Now;
                double maxWaitSec = (double)numTimeout.Value;
                bool biteDetected = false;

                while (isRunning && (DateTime.Now - waitStart).TotalSeconds < maxWaitSec)
                {
                    int redCount;
                    if (ScanForStopButtonFast(out redCount))
                    {
                        Thread.Sleep(25);
                        if (ScanForStopButtonFast(out redCount))
                        {
                            biteDetected = true;
                            break;
                        }
                    }
                    Thread.Sleep(25);
                }

                if (!isRunning) break;

                // 4. Hook & Reel OR Clean Timeout Reset
                if (biteDetected)
                {
                    ReelInFish();
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

                    Thread.Sleep(1300);
                }
            }
            catch (Exception ex)
            {
                Log("Loop error: " + ex.Message);
                Thread.Sleep(1000);
            }
        }
    }

    private void ReelInFish()
    {
        InvokeAction(delegate {
            lblStatus.Text = "STATUS: BITE DETECTED! TURBO REELING!";
            lblStatus.ForeColor = Color.FromArgb(166, 227, 161);
        });

        int clickSpeed = (int)numClickSpeedMs.Value;
        int maxCap = (int)numClicksCount.Value;
        int clicksSent = 0;

        Log("Bite confirmed! Turbo clicking while STOP button is active...");

        while (isRunning && clicksSent < maxCap)
        {
            if (clicksSent >= 4)
            {
                int redCount;
                if (!ScanForStopButtonFast(out redCount))
                {
                    Thread.Sleep(25);
                    if (!ScanForStopButtonFast(out redCount))
                    {
                        break;
                    }
                }
            }

            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(10);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            Thread.Sleep(clickSpeed);
            clicksSent++;
        }

        fishCaught++;
        InvokeAction(delegate {
            lblFishCount.Text = string.Format("Fish Caught: {0}", fishCaught);
            lblStatus.Text = "STATUS: Fish Caught! Waiting for reward popup...";
            lblStatus.ForeColor = Color.FromArgb(137, 220, 235);
        });

        int postWaitMs = (int)(numPostCatchWait.Value * 1000m);
        Log(string.Format("Caught Fish #{0} in {1} clicks! Waiting {2}ms for popup to finish...", fishCaught, clicksSent, postWaitMs));

        Thread.Sleep(postWaitMs);
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
        char c = char.ToUpper(text.Trim()[0]);
        if (c >= '0' && c <= '9') return (byte)(0x30 + (c - '0'));
        if (c >= 'A' && c <= 'Z') return (byte)(0x41 + (c - 'A'));
        return fallback;
    }

    private void InvokeAction(Action act)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        this.BeginInvoke(act);
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
        RegisterHotKey(this.Handle, HOTKEY_ID_F6, 0, VK_F6);
        RegisterHotKey(this.Handle, HOTKEY_ID_F7, 0, VK_F7);
        RegisterHotKey(this.Handle, HOTKEY_ID_F8, 0, VK_F8);
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
        UnregisterHotKey(this.Handle, HOTKEY_ID_F6);
        UnregisterHotKey(this.Handle, HOTKEY_ID_F7);
        UnregisterHotKey(this.Handle, HOTKEY_ID_F8);
        isRunning = false;
        if (botThread != null && botThread.IsAlive)
        {
            botThread.Abort();
        }
        base.OnFormClosing(e);
    }

    [STAThread]
    public static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Control.CheckForIllegalCrossThreadCalls = false;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
