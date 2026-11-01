using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace Cybrslc.Workshop
{
    internal static class MemeProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--lab-marker")
                return LabPersistence.Execute(delegate { LabPersistence.WriteMarker(args[1]); }, false);
            if (args.Length == 1 && args[0] == "--remove-lab-persistence")
                return LabPersistence.Execute(LabPersistence.Remove, true);
            if (args.Length != 0)
            {
                Console.Error.WriteLine("Unknown arguments. Open without arguments for the consent-based lab controls.");
                return 2;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MemeController());
            return 0;
        }
    }

    internal sealed class MemeController : Form
    {
        private const string WorkshopId = "CYBRSLC&CDRU_BENIGN_MEME_DEMO_V5";
        private readonly Label status;
        private readonly Button startButton;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly Timer sequenceTimer;
        private readonly List<Form> popups = new List<Form>();
        private readonly List<Image> memes = new List<Image>();
        private readonly List<string> events = new List<string>();
        private AnimatedDemoWindow terminal;
        private AnimatedDemoWindow notes;
        private int step;
        private bool stopping;
        private bool started;
        private readonly System.Threading.EventWaitHandle workerStop;
        private readonly List<Process> workers = new List<Process>();
        private readonly string workerStopName;
        private readonly Timer repeatTimer = new Timer();
        private readonly Timer healthTimer = new Timer();
        private readonly TextBox workerStatus = new TextBox();
        private readonly CheckBox enableSound = new CheckBox();
        private readonly List<WorkerStatus> workerStates = new List<WorkerStatus>();
        private readonly SoundPlayer sound = new SoundPlayer();
        private readonly MemoryStream soundData;
        private int memeIndex;
        private int cycle;
        private readonly Random placementRandom = new Random();
        private int scatterOffset;
        private string workerLaunchError = "";

        internal MemeController()
        {
            workerStopName = "Local\\CYBRSLC&CDRU-Stop-" + Guid.NewGuid().ToString("N");
            workerStop = new System.Threading.EventWaitHandle(false,
                System.Threading.EventResetMode.ManualReset, workerStopName);
            Text = "DoNtStopMePorfavor | CYBRSLC&CDRU lab";
            ClientSize = new Size(760, 760);
            MinimumSize = new Size(776, 799);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(16, 23, 34);
            ForeColor = Color.WhiteSmoke;
            Font = new Font("Segoe UI", 10);
            Icon = SystemIcons.Information;
            KeyPreview = true;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24);
            layout.ColumnCount = 1;
            layout.RowCount = 9;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(layout);

            Label badge = MakeLabel("CYBRSLC&CDRU  /  CONSENT-BASED LAB DEMO", 10, Color.Aquamarine);
            layout.Controls.Add(badge, 0, 0);
            Label title = MakeLabel("Your firewall needs a therapist.", 23, Color.White);
            title.Font = new Font("Segoe UI", 23, FontStyle.Bold);
            layout.Controls.Add(title, 0, 1);
            layout.Controls.Add(MakeLabel(
                "Offline memes, fake terminal drama, and actual Notepad + Calculator.\r\n" +
                "One ridiculously named process. Zero infection.", 11, Color.LightGray), 0, 2);
            layout.Controls.Add(MakeLabel(
                "Start opens up to FIVE demo windows and launches Notepad + Calculator once.\r\n" +
                "Typing and commands are VISUAL ONLY in our own windows. No scripts run.\r\n" +
                "Also starts SIX harmless lab worker processes, including Windows lookalike names.",
                10, Color.LightSteelBlue), 0, 3);

            status = MakeLabel(
                "READY - nothing opened yet.\r\n\r\n" +
                "Memes are bundled in this EXE; no downloads.\r\n" +
                "Five fresh popups every 45 seconds after the intro until YOU stop it.\r\n" +
                "Popup X closes it until next batch. Esc / Stop ALL ends the demo.\r\n" +
                "Persistence requires separate opt-in. No global keystrokes or document edits.",
                10, Color.Aquamarine);
            layout.Controls.Add(status, 0, 4);
            workerStatus.Multiline = true;
            workerStatus.ReadOnly = true;
            workerStatus.ScrollBars = ScrollBars.Vertical;
            workerStatus.Dock = DockStyle.Fill;
            workerStatus.Text = "V3 worker diagnostics - no workers started before consent.";
            workerStatus.BackColor = Color.FromArgb(25, 35, 49);
            workerStatus.ForeColor = Color.White;
            layout.Controls.Add(workerStatus, 0, 5);
            enableSound.Text = "Enable quiet goofy sound (optional; uncheck to mute immediately)";
            enableSound.Dock = DockStyle.Fill;
            enableSound.CheckedChanged += delegate { if (!enableSound.Checked) sound.Stop(); };
            layout.Controls.Add(enableSound, 0, 6);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            startButton = MakeButton("I agree - start offline chaos", 230, Color.FromArgb(24, 100, 88));
            startButton.Click += StartDemo;
            Button stopButton = MakeButton("Stop & Exit", 120, Color.FromArgb(120, 43, 62));
            stopButton.Click += delegate { Close(); };
            buttons.Controls.Add(startButton);
            buttons.Controls.Add(stopButton);
            Button persistenceButton = MakeButton("Persistence lab...", 155, Color.FromArgb(43, 65, 110));
            persistenceButton.Click += delegate { LabPersistence.ShowControls(this); };
            buttons.Controls.Add(persistenceButton);
            Button keyboardButton = MakeButton("Keyboard lab...", 155, Color.FromArgb(43, 65, 110));
            keyboardButton.Click += delegate
            {
                using (KeyboardLab dialog = new KeyboardLab()) dialog.ShowDialog(this);
            };
            buttons.Controls.Add(keyboardButton);
            layout.Controls.Add(buttons, 0, 7);
            layout.Controls.Add(MakeLabel(WorkshopId + " | 45-second repeat", 8, Color.Gray), 0, 8);
            CancelButton = stopButton;

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show lab controls", null, delegate { RestoreControls(); });
            trayMenu.Items.Add("Stop & Exit", null, delegate { Close(); });
            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Information;
            tray.Text = "CYBRSLC&CDRU meme demo - right-click to stop";
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { RestoreControls(); };
            sequenceTimer = new Timer();
            sequenceTimer.Interval = 1800;
            sequenceTimer.Tick += AdvanceSequence;
            repeatTimer.Interval = 45000;
            repeatTimer.Tick += delegate { RepeatContent(); };
            healthTimer.Interval = 1000;
            healthTimer.Tick += delegate { RefreshWorkers(); };
            soundData = CreateGoofySound();
            sound.Stream = soundData;
            sound.Load();
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    tray.Visible = true;
                    Hide();
                }
            };
        }

        private static Label MakeLabel(string text, float size, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.UseMnemonic = false;
            label.Font = new Font("Segoe UI", size);
            label.ForeColor = color;
            label.Dock = DockStyle.Fill;
            return label;
        }

        private static Button MakeButton(string text, int width, Color color)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, 43);
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(0, 0, 12, 0);
            return button;
        }

        private void StartDemo(object sender, EventArgs e)
        {
            if (started) return;
            started = true;
            startButton.Enabled = false;
            if (!LoadMemes()) return;
            if (!StartWorkers()) return;
            Record("RUNNING - Stop & Exit or Esc ends the demo.");
            sequenceTimer.Start();
            AdvanceSequence(this, EventArgs.Empty);
        }

        private bool LoadMemes()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resources = assembly.GetManifestResourceNames();
            Array.Sort(resources, StringComparer.Ordinal);
            foreach (string name in resources)
            {
                if (!name.StartsWith("Meme.", StringComparison.Ordinal)) continue;
                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null) throw new InvalidOperationException("Missing embedded image: " + name);
                        using (Image image = Image.FromStream(stream))
                        {
                            memes.Add(new Bitmap(image));
                        }

                    }
                }
                catch (ArgumentException exception)
                {
                    Record("IMAGE ERROR (" + name + "): " + exception.Message);
                    return false;
                }
                catch (InvalidOperationException exception)
                {
                    Record("RESOURCE ERROR: " + exception.Message);
                    return false;
                }
            }
            if (memes.Count == 0)
            {
                Record("ERROR: no embedded memes. Rebuild with Build-Prank.ps1. No sequence started.");
                return false;
            }
            memes.Add(CreateApplicationCard());
            memes.Add(CreateFriesCard());
            return true;
        }

        private static Bitmap CreateApplicationCard()
        {
            Bitmap card = new Bitmap(900, 600);
            using (Graphics graphics = Graphics.FromImage(card))
            using (Font heading = new Font("Segoe UI", 30, FontStyle.Bold))
            using (Font body = new Font("Segoe UI", 23))
            {
                graphics.Clear(Color.FromArgb(155, 20, 33));
                graphics.DrawString("THE McDONALD'S JOB APPLICATION", heading, Brushes.Gold,
                    new RectangleF(40, 35, 820, 110));
                graphics.DrawString("When you click the phishing link\nfor the third time this week.\n\n" +
                    "Position: Senior French Fry Analyst\nExperience: incident response (to ketchup)\n" +
                    "Availability: after the SOC shift\n\nPARODY ONLY - NOT A REAL APPLICATION\nNo form. No personal information collected.",
                    body, Brushes.White, new RectangleF(40, 165, 820, 410));
            }
            return card;
        }

        private static Bitmap CreateFriesCard()
        {
            Bitmap card = new Bitmap(900, 600);
            using (Graphics graphics = Graphics.FromImage(card))
            using (Font heading = new Font("Segoe UI", 42, FontStyle.Bold))
            using (Font body = new Font("Segoe UI", 25))
            using (Font footer = new Font(FontFamily.GenericSansSerif, 12))
            {
                graphics.Clear(Color.FromArgb(25, 30, 40));
                graphics.DrawString("ME: \"I basically work in cybersecurity.\"\nAlso me: disables the firewall to fix Wi-Fi.",
                    body, Brushes.White, new RectangleF(40, 35, 820, 150));
                for (int i = 0; i < 7; i++)
                    graphics.FillRectangle(Brushes.Gold, 330 + i * 32, 220 + (i % 3) * 12, 23, 120);
                graphics.FillRectangle(Brushes.Firebrick, 310, 305, 260, 100);
                graphics.DrawString("PUT THE FRIES IN THE BAG", heading, Brushes.Gold,
                    new RectangleF(40, 420, 820, 140));
                graphics.DrawString("Original CYBRSLC&CDRU parody - no affiliation or real application",
                    footer, Brushes.LightGray,
                    new RectangleF(40, 570, 820, 25));
            }
            return card;
        }

        private static MemoryStream CreateGoofySound()
        {
            const int rate = 22050;
            const int samples = rate * 3 / 5;
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(samples * 2);
            double phase = 0;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / rate;
                phase += 2 * Math.PI * (420 - 290 * t / 0.6) / rate;
                double envelope = Math.Sin(Math.PI * i / samples) *
                    (0.5 + 0.5 * Math.Sin(2 * Math.PI * 11 * t));
                writer.Write((short)(Math.Sin(phase) * envelope * 3500));
            }
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        private void PlaySound()
        {
            if (!enableSound.Checked) return;
            try { sound.Play(); }
            catch (InvalidOperationException exception)
            {
                enableSound.Checked = false;
                Record("SOUND ERROR: " + exception.Message);
            }
        }

        private void RepeatContent()
        {
            if (stopping) return;
            ReplacePopupBatch(true);
            PlaySound();
            Record("Popup batch " + (++cycle) + ". Next in 45 seconds; no app/worker relaunches.");
        }

        private void ReplacePopupBatch(bool show)
        {
            if (stopping) return;
            string terminalScript = terminal == null ? "DISPLAY ONLY - no commands executed." : terminal.Script;
            string notesScript = notes == null ? "DEMO TEXT - nothing is saved." : notes.Script;
            foreach (Form popup in popups)
            {
                popup.Close();
                popup.Dispose();
            }
            popups.Clear();
            scatterOffset = (scatterOffset + placementRandom.Next(1, 5)) % 5;
            terminal = new AnimatedDemoWindow("FAKE PowerShell | DISPLAY ONLY", terminalScript, true, delegate { Close(); });
            notes = new AnimatedDemoWindow("FAKE Notepad | DEMO-OWNED TEXT", notesScript, false, delegate { Close(); });
            ShowPopup(terminal, show);
            ShowPopup(notes, show);
            for (int i = 0; i < 3; i++)
                ShowPopup(new MemeWindow(memes[memeIndex++ % memes.Count], delegate { Close(); }), show);
        }

        private bool StartWorkers()
        {
            string[] names = { "FakeUpdate", "MemeWroker", "SnackScaner", "svch0st", "Isass", "csrsss" };
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workers");
            foreach (string name in names)
            {
                if (!File.Exists(Path.Combine(folder, name + ".exe")))
                {
                    workerStatus.Text = "MISSING: " + Path.Combine(folder, name + ".exe") +
                        "\r\nCopy ALL of bin, including Workers. Do not bypass security blocks.";
                    Record("WORKERS MISSING: copy the entire bin folder, including Workers. No sequence started.");
                    return false;
                }
            }
            using (Process current = Process.GetCurrentProcess())
            {
                foreach (string name in names)
                {
                    try
                    {
                        ProcessStartInfo start = new ProcessStartInfo(Path.Combine(folder, name + ".exe"));
                        start.UseShellExecute = false;
                        start.CreateNoWindow = true;
                        start.RedirectStandardError = true;
                        start.RedirectStandardOutput = true;
                        start.Arguments = "--cybrslc-demo " + current.Id + " " +
                            current.StartTime.ToUniversalTime().Ticks + " " + workerStopName;
                        Process worker = Process.Start(start);
                        if (worker == null) throw new InvalidOperationException("No worker process returned.");
                        workers.Add(worker);
                        WorkerStatus state = new WorkerStatus(worker, name);
                        workerStates.Add(state);
                        worker.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null) lock (state) { state.Message = e.Data; }
                        };
                        worker.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null) lock (state) { state.Message = e.Data; }
                        };
                        worker.BeginOutputReadLine();
                        worker.BeginErrorReadLine();
                    }
                    catch (Win32Exception exception)
                    {
                        workerStop.Set();
                        Record("WORKER LAUNCH ERROR: " + name + ": " + exception.Message);
                        workerLaunchError = "LAUNCH FAILED " + name + ": " + exception.Message;
                        RefreshWorkers();
                        healthTimer.Start();
                        return false;
                    }
                    catch (InvalidOperationException exception)
                    {
                        workerStop.Set();
                        Record("WORKER LAUNCH ERROR: " + name + ": " + exception.Message);
                        workerLaunchError = "LAUNCH FAILED " + name + ": " + exception.Message;
                        RefreshWorkers();
                        healthTimer.Start();
                        return false;
                    }
                }
            }
            Record("Six CYBRSLC&CDRU lab workers launched. Details > Command line identifies them.");
            healthTimer.Start();
            RefreshWorkers();
            return true;
        }

        private void RefreshWorkers()
        {
            StringBuilder lines = new StringBuilder("Task Manager > Details: match these PIDs (no respawns).\r\n");
            if (workerLaunchError.Length > 0) lines.AppendLine(workerLaunchError);
            foreach (WorkerStatus state in workerStates)
            {
                lock (state)
                {
                    lines.AppendLine(state.Name + ".exe | PID " + state.Process.Id + " | " +
                        (state.Process.HasExited ? "EXITED code " + state.Process.ExitCode : "RUNNING") +
                        " | " + (state.Message == "Waiting for READY handshake" &&
                            state.Started.ElapsedMilliseconds > 10000
                            ? "NO READY after 10s: slow startup or stale worker build; verify full bin copy."
                            : state.Message));
                }
            }
            workerStatus.Text = lines.ToString();
        }

        private void AdvanceSequence(object sender, EventArgs e)
        {
            if (stopping) return;
            switch (step++)
            {
                case 0:
                    PlaySound();
                    terminal = new AnimatedDemoWindow("FAKE PowerShell | DISPLAY ONLY",
                        "PS [DEMO]> Connect-SnackServer -Authentication TrustMeBro\r\n" +
                        "[FICTION] Access denied: snacks protected by MFA.\r\n\r\n" +
                        "PS [DEMO]> Install-ExtraRAM -Payment StudentDiscount\r\n" +
                        "[FICTION] Purchase declined. Try downloading motivation.\r\n\r\n" +
                        "PS [DEMO]> Invoke-CyberDrama -ThreatLevel GroupProject\r\n" +
                        "[FICTION] Privileges escalated to unpaid team leader.\r\n\r\n" +
                        "NO COMMANDS EXECUTED. These are animated characters, not a shell.",
                        true, delegate { Close(); });
                    ShowPopup(terminal);
                    break;
                case 1:
                    ShowMeme(0);
                    break;
                case 2:
                    LaunchApp("notepad.exe");
                    break;
                case 3:
                    notes = new AnimatedDemoWindow("FAKE Notepad | DEMO-OWNED TEXT",
                        "Dear incident response team,\r\n\r\n" +
                        "The firewall has requested emotional support.\r\n" +
                        "The SOC analyst is now monitoring the snack perimeter.\r\n" +
                        "Password strength: emotionally unavailable.\r\n" +
                        "Root cause: somebody clicked the forbidden group-project link.\r\n\r\n" +
                        "ACTION ITEMS:\r\n- Patch the server.\r\n- Hydrate the analyst.\r\n" +
                        "- Stop naming passwords after the Wi-Fi.\r\n\r\n" +
                        "This is not a real document. Nothing is saved or typed into other apps.",
                        false, delegate { Close(); });
                    ShowPopup(notes);
                    break;
                case 4:
                    ShowMeme(1);
                    break;
                case 5:
                    LaunchApp("calc.exe");
                    break;
                case 6:
                    ShowMeme(2);
                    break;
                case 7:
                    int index = 3;
                    foreach (Form popup in popups)
                    {
                        MemeWindow meme = popup as MemeWindow;
                        if (meme != null && !meme.IsDisposed && index < memes.Count)
                            meme.SetImage(memes[index++]);
                    }
                    Record("Second meme batch shown in existing windows (no reopening).");
                    break;
                case 8:
                    int last = 6;
                    foreach (Form popup in popups)
                    {
                        MemeWindow meme = popup as MemeWindow;
                        if (meme != null && !meme.IsDisposed && last < memes.Count)
                            meme.SetImage(memes[last++]);
                    }
                    Record("Final meme batch shown. Waiting for visual typing to finish.");
                    break;
                default:
                    if ((terminal == null || terminal.IsDisposed || terminal.Completed) &&
                        (notes == null || notes.IsDisposed || notes.Completed))
                    {
                        sequenceTimer.Stop();
                        memeIndex = 0;
                        foreach (Form popup in popups)
                        {
                            MemeWindow meme = popup as MemeWindow;
                            if (meme != null && !meme.IsDisposed)
                            {
                                meme.SetImage(memes[memes.Count - 1]);
                                break;
                            }
                        }
                        repeatTimer.Start();
                        Record("Intro complete. First fresh popup batch in 45 seconds.\r\n" +
                            "Closed popups return next batch. Stop ends demo and workers.");
                    }
                    break;
            }
        }

        private void ShowMeme(int index)
        {
            if (index >= memes.Count) return;
            ShowPopup(new MemeWindow(memes[index], delegate { Close(); }));
        }

        private void ShowPopup(Form popup, bool show = true)
        {
            // Dispose the previous batch before allocating its replacement.
            if (stopping || popups.Count >= 5)
            {
                popup.Dispose();
                return;
            }
            Rectangle area = Screen.FromControl(this).WorkingArea;
            popup.StartPosition = FormStartPosition.Manual;
            popup.Bounds = GetScatterBounds(area, popups.Count);
            popups.Add(popup);
            if (show) popup.Show(this);
            Record("Opened: " + popup.Text);
        }

        private Rectangle GetScatterBounds(Rectangle area, int index)
        {
            int width = Math.Min(490, Math.Max(1, area.Width / 2));
            int height = Math.Min(340, Math.Max(1, area.Height / 2));
            int horizontal = area.Width - width;
            int vertical = area.Height - height;
            int slot = (index + scatterOffset) % 5;
            int x = slot == 4 ? horizontal / 2 : (slot % 2) * horizontal;
            int y = slot == 4 ? vertical / 2 : (slot / 2) * vertical;
            x = Math.Max(0, Math.Min(horizontal, x + placementRandom.Next(-20, 21)));
            y = Math.Max(0, Math.Min(vertical, y + placementRandom.Next(-20, 21)));
            return new Rectangle(area.Left + x, area.Top + y, width, height);
        }

        private void LaunchApp(string executable)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), executable);
                using (Process process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
                {
                    // Packaged Windows apps can delegate activation without returning their final process.
                }
                Record("Launch requested once: " + executable + ". No keys sent; close it yourself.");
            }
            catch (Win32Exception exception)
            {
                Record("APP LAUNCH ERROR (" + executable + "): " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                Record("APP LAUNCH ERROR (" + executable + "): " + exception.Message);
            }
        }

        private void Record(string message)
        {
            events.Add(message);
            if (events.Count > 5) events.RemoveAt(0);
            status.Text = String.Join("\r\n\r\n", events.ToArray());
            status.AccessibleDescription = status.Text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            stopping = true;
            workerStop.Set();
            sequenceTimer.Stop();
            repeatTimer.Stop();
            healthTimer.Stop();
            sound.Stop();
            foreach (Form popup in popups)
                if (!popup.IsDisposed) popup.Close();
            base.OnFormClosing(e);
        }

        private void RestoreControls()
        {
            Show();
            WindowState = FormWindowState.Normal;
            tray.Visible = false;
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (sequenceTimer != null) sequenceTimer.Dispose();
                repeatTimer.Dispose();
                healthTimer.Dispose();
                sound.Stop();
                sound.Dispose();
                if (soundData != null) soundData.Dispose();
                if (workerStop != null)
                {
                    workerStop.Set();
                    workerStop.Dispose();
                }

                foreach (Process worker in workers) worker.Dispose();
                foreach (Form popup in popups) popup.Dispose();
                foreach (Image image in memes) image.Dispose();
                if (tray != null)
                {
                    tray.Visible = false;
                    tray.Dispose();
                }
                if (trayMenu != null) trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class KeyboardLab : Form
    {
        private readonly CheckBox consent = new CheckBox();
        private readonly TextBox input = new TextBox();
        private readonly TextBox output = new TextBox();
        private readonly Queue<string> entries = new Queue<string>();
        private const int MaximumEvents = 500;

        internal KeyboardLab()
        {
            Text = "CYBRSLC&CDRU - visible keyboard exercise";
            ClientSize = new Size(700, 490);
            MinimumSize = new Size(716, 529);
            StartPosition = FormStartPosition.CenterParent;
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);
            Label explanation = new Label();
            explanation.Dock = DockStyle.Fill;
            explanation.Text = "Type dummy text ONLY in the input box below. Never enter passwords or personal data.\r\n" +
                "No other window, application, clipboard, or background keyboard input is monitored.\r\n" +
                "Events stay in memory (latest 500) unless you explicitly export a local text file.";
            layout.Controls.Add(explanation, 0, 0);
            consent.Text = "I consent - capture typing in this input box";
            consent.Dock = DockStyle.Fill;
            consent.CheckedChanged += delegate
            {
                input.Enabled = consent.Checked;
                if (consent.Checked) input.Focus();
            };
            layout.Controls.Add(consent, 0, 1);
            input.Multiline = true;
            input.Dock = DockStyle.Fill;
            input.Enabled = false;
            input.MaxLength = 2000;
            input.ShortcutsEnabled = false;
            input.KeyPress += CaptureCharacter;
            layout.Controls.Add(input, 0, 2);
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Vertical;
            output.Dock = DockStyle.Fill;
            layout.Controls.Add(output, 0, 3);
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            Button stop = new Button();
            stop.Text = "Stop";
            stop.Click += delegate { consent.Checked = false; };
            Button clear = new Button();
            clear.Text = "Clear";
            clear.Click += delegate { ClearCapture(); };
            Button export = new Button();
            export.Text = "Export log...";
            export.AutoSize = true;
            export.Click += ExportLog;
            Button close = new Button();
            close.Text = "Close";
            close.Click += delegate { Close(); };
            buttons.Controls.AddRange(new Control[] { stop, clear, export, close });
            layout.Controls.Add(buttons, 0, 4);
            Label footer = new Label();
            footer.Dock = DockStyle.Fill;
            footer.Text = "Switching away stops capture; opt in again to resume. Clear/Close discard in-memory data.\r\n" +
                "Previously exported files remain on disk; delete them separately when finished.";
            layout.Controls.Add(footer, 0, 5);
            CancelButton = close;
            Deactivate += delegate { consent.Checked = false; };
            FormClosed += delegate { ClearCapture(); };
        }

        private void CaptureCharacter(object sender, KeyPressEventArgs e)
        {
            if (!consent.Checked || !input.Focused) return;
            string value;
            switch (e.KeyChar)
            {
                case '\r': value = "[ENTER]"; break;
                case '\b': value = "[BACKSPACE]"; break;
                case '\t': value = "[TAB]"; break;
                default:
                    value = Char.IsControl(e.KeyChar) ? "[CONTROL U+" + ((int)e.KeyChar).ToString("X4") + "]" :
                        "'" + e.KeyChar + "' (U+" + ((int)e.KeyChar).ToString("X4") + ")";
                    break;
            }
            entries.Enqueue(DateTime.UtcNow.ToString("O") + " INPUT " + value);
            while (entries.Count > MaximumEvents) entries.Dequeue();
            output.Text = String.Join(Environment.NewLine, entries.ToArray());
            output.SelectionStart = output.TextLength;
            output.ScrollToCaret();
        }

        private void ClearCapture()
        {
            consent.Checked = false;
            input.Clear();
            output.Clear();
            entries.Clear();
        }

        private void ExportLog(object sender, EventArgs e)
        {
            consent.Checked = false;
            if (entries.Count == 0)
            {
                MessageBox.Show(this, "No captured events to export.", "Keyboard lab");
                return;
            }
            using (SaveFileDialog save = new SaveFileDialog())
            {
                save.Title = "Export dummy workshop input - local file only";
                save.Filter = "Text files (*.txt)|*.txt";
                save.FileName = "CYBRSLC&CDRU-keyboard-lab.txt";
                save.DefaultExt = "txt";
                save.AddExtension = true;
                save.OverwritePrompt = true;
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(save.FileName,
                        "CYBRSLC&CDRU opt-in keyboard exercise - input box only; latest 500 character events.\r\n" +
                        output.Text + Environment.NewLine);
                    MessageBox.Show(this, "Exported to:\r\n" + save.FileName, "Keyboard lab");
                }
                catch (IOException exception) { ExportError(exception); }
                catch (UnauthorizedAccessException exception) { ExportError(exception); }
                catch (System.Security.SecurityException exception) { ExportError(exception); }
            }
        }

        private void ExportError(Exception exception)
        {
            MessageBox.Show(this, "Log export failed: " + exception.Message, "Keyboard lab",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class WorkerStatus
    {
        internal readonly Process Process;
        internal readonly string Name;
        internal readonly Stopwatch Started = Stopwatch.StartNew();
        internal string Message = "Waiting for READY handshake";
        internal WorkerStatus(Process process, string name) { Process = process; Name = name; }
    }

    internal sealed class MemeWindow : Form
    {
        private readonly PictureBox picture;

        internal MemeWindow(Image image, Action stop)
        {
            Text = "CYBRSLC&CDRU meme | X: close until next batch | Stop ALL: exit";
            BackColor = Color.FromArgb(16, 23, 34);
            picture = new PictureBox();
            picture.Dock = DockStyle.Fill;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Image = image;
            Button exit = new Button();
            exit.Text = "Stop ALL demo windows";
            exit.Dock = DockStyle.Bottom;
            exit.Height = 38;
            exit.Click += delegate { stop(); };
            Controls.Add(picture);
            Controls.Add(exit);
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) stop();
            };
        }

        internal void SetImage(Image image)
        {
            picture.Image = image;
        }
    }

    internal sealed class AnimatedDemoWindow : Form
    {
        private readonly TextBox text;
        private readonly Timer typing;
        private readonly string content;
        internal string Script { get { return content; } }
        private int position;
        internal bool Completed { get { return position >= content.Length; } }

        internal AnimatedDemoWindow(string title, string script, bool dark, Action stop)
        {
            Text = title;
            content = script;
            text = new TextBox();
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.Dock = DockStyle.Fill;
            text.Font = new Font("Consolas", 11);
            text.BackColor = dark ? Color.FromArgb(10, 22, 43) : Color.White;
            text.ForeColor = dark ? Color.LightGreen : Color.Black;
            Button exit = new Button();
            exit.Text = "VISUAL SIMULATION ONLY - Stop ALL";
            exit.Dock = DockStyle.Bottom;
            exit.Height = 38;
            exit.Click += delegate { stop(); };
            Controls.Add(text);
            Controls.Add(exit);
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) stop();
            };
            typing = new Timer();
            typing.Interval = 55;
            typing.Tick += delegate
            {
                int length = Math.Min(4, content.Length - position);
                if (length > 0)
                {
                    text.AppendText(content.Substring(position, length));
                    position += length;
                }

                if (Completed) typing.Stop();
            };
            Shown += delegate { typing.Start(); };
            FormClosed += delegate { typing.Stop(); };
        }

        internal void Restart()
        {
            typing.Stop();
            text.Clear();
            position = 0;
            typing.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && typing != null) typing.Dispose();
            base.Dispose(disposing);
        }
    }
}
