using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DigitalArts.BatteryGuard
{
    sealed class MainForm : Form
    {
        // ---- स्थिति ----
        readonly NotifyIcon tray;
        readonly Timer pollTimer, soundTimer;
        readonly AlarmSound sound = new AlarmSound();
        AlertForm alert, testAlert;
        AlertKind lastCondition = AlertKind.None;
        bool skipUntilResolved;
        DateTime nextAlertAt = DateTime.MinValue;
        DateTime pausedUntil = DateTime.MinValue;
        bool allowShow, exiting;
        int iconPct = int.MinValue; bool iconPlugged; Icon trayIconImg;
        BatterySnapshot lastSnapshot;
        HealthInfo lastHealth;      // null = अभी जाँच हो रही है

        // ---- अनुवाद: हर स्थिर टेक्स्ट (हिंदी, English) यहाँ दर्ज होता है ----
        readonly List<(Control c, string hi, string en)> locControls = new List<(Control, string, string)>();
        readonly List<(ToolStripItem c, string hi, string en)> locMenu = new List<(ToolStripItem, string, string)>();

        // ---- UI ----
        Label lblPercent, lblStatus, lblTime, lblHealth, lblHealthDetail, lblPause;
        BatteryBar bar;
        NumericUpDown numHigh, numLow, numRepeat;
        CheckBox chkSound, chkAuto;
        Button btnHealthRefresh, btnReport, btnHindi, btnEnglish;
        ToolStripMenuItem miPause;
        HeartControl heart;

        public MainForm(bool startMinimized)
        {
            allowShow = !startMinimized;

            Settings.Load();
            if (Settings.FirstRun) { Settings.AutoStart = true; Settings.Save(); }
            AutoStartHelper.Apply(Settings.AutoStart); // हर बार सही exe पाथ दोबारा लिखें

            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            if (appIcon != null) Icon = appIcon;

            // ---- ट्रे आइकन ----
            var menu = new ContextMenuStrip { Font = new Font(Brand.FontName, 9.5f) };
            var miOpen = LocMenu(new ToolStripMenuItem("", null, (s, e) => ShowFromTray()), "Battery Guard खोलें", "Open Battery Guard");
            miOpen.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(miOpen);
            menu.Items.Add(LocMenu(new ToolStripMenuItem("", null, (s, e) => { ShowFromTray(); RefreshHealth(); }), "बैटरी हेल्थ जाँचें", "Check battery health"));
            menu.Items.Add(new ToolStripSeparator());
            miPause = new ToolStripMenuItem("", null, (s, e) => TogglePause());
            menu.Items.Add(miPause);
            var miLang = new ToolStripMenuItem("", null, (s, e) => SetLanguage(Lang.En ? "hi" : "en"));
            menu.Items.Add(LocMenu(miLang, "Switch to English", "हिंदी में बदलें"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(LocMenu(new ToolStripMenuItem("", null, (s, e) => ShowAbout()), Brand.Company + " के बारे में", "About " + Brand.Company));
            menu.Items.Add(LocMenu(new ToolStripMenuItem("", null, (s, e) => ExitApp()), "बंद करें (Exit)", "Exit"));

            tray = new NotifyIcon { ContextMenuStrip = menu, Text = Brand.AppName, Visible = true, Icon = appIcon ?? SystemIcons.Application };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            tray.BalloonTipClicked += (s, e) => ShowFromTray();
            UpdatePauseText();

            // ---- टाइमर ----
            pollTimer = new Timer { Interval = 20_000 };
            pollTimer.Tick += (s, e) => CheckBattery();
            soundTimer = new Timer { Interval = 45_000 }; // आवाज़ अधिकतम 45 सेकंड, पॉपअप बना रहेगा
            soundTimer.Tick += (s, e) => { soundTimer.Stop(); sound.Stop(); };

            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            if (!IsHandleCreated) CreateHandle();
            CheckBattery();
            pollTimer.Start();
            RefreshHealth();

            if (startMinimized && !Settings.TrayHintShown) ShowTrayHint();
        }

        // =====================================================================
        //  भाषा
        // =====================================================================
        T Loc<T>(T c, string hi, string en) where T : Control
        {
            locControls.Add((c, hi, en));
            c.Text = Lang.T(hi, en);
            return c;
        }

        T LocMenu<T>(T item, string hi, string en) where T : ToolStripItem
        {
            locMenu.Add((item, hi, en));
            item.Text = Lang.T(hi, en);
            return item;
        }

        void SetLanguage(string lang)
        {
            if (Settings.Language == lang) return;
            Settings.Language = lang;
            Settings.Save();
            ApplyLanguage();
        }

        void ApplyLanguage()
        {
            SuspendLayout();
            foreach (var (c, hi, en) in locControls) c.Text = Lang.T(hi, en);
            foreach (var (c, hi, en) in locMenu) c.Text = Lang.T(hi, en);
            Text = $"{Brand.AppName} – {Brand.Company}";
            StyleLangButtons();
            UpdatePauseText();
            UpdateStatusUi(lastSnapshot);
            RenderHealth(lastHealth);
            iconPct = int.MinValue; UpdateTrayIcon(lastSnapshot);
            ResumeLayout(true);
            alert?.ApplyLanguage();
            testAlert?.ApplyLanguage();
        }

        void StyleLangButtons()
        {
            StyleToggle(btnHindi, !Lang.En);
            StyleToggle(btnEnglish, Lang.En);
        }

        static void StyleToggle(Button b, bool active)
        {
            b.BackColor = active ? Color.White : Brand.Primary;
            b.ForeColor = active ? Brand.PrimaryDk : Color.FromArgb(220, 228, 255);
            b.FlatAppearance.BorderColor = Color.White;
            b.Font = new Font(Brand.FontName, 9f, active ? FontStyle.Bold : FontStyle.Regular);
        }

        // =====================================================================
        //  बैटरी जाँच और अलार्म
        // =====================================================================
        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // चार्जर लगाने/निकालने पर तुरंत प्रतिक्रिया
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)CheckBattery);
        }

        void CheckBattery()
        {
            if (exiting) return;
            var b = BatterySnapshot.Read();
            lastSnapshot = b;
            UpdateStatusUi(b);
            UpdateTrayIcon(b);

            if (pausedUntil != DateTime.MinValue && DateTime.Now >= pausedUntil) SetPaused(false);

            AlertKind cond = AlertKind.None;
            if (b.HasBattery)
            {
                if (b.PluggedIn && b.Percent >= Settings.High) cond = AlertKind.High;
                else if (!b.PluggedIn && b.Percent <= Settings.Low) cond = AlertKind.Low;
            }

            if (cond != lastCondition)
            {
                // स्थिति बदल गई (जैसे यूज़र ने चार्जर निकाल दिया) – पुराना अलार्म हटाएँ
                lastCondition = cond;
                skipUntilResolved = false;
                nextAlertAt = DateTime.MinValue;
                CloseAlert();
            }

            if (cond == AlertKind.None) return;
            if (alert != null) { alert.UpdatePercent(b.Percent); return; }
            if (skipUntilResolved || DateTime.Now < pausedUntil || DateTime.Now < nextAlertAt) return;

            ShowAlert(cond, b.Percent);
        }

        void ShowAlert(AlertKind kind, int pct)
        {
            CloseTestAlert();
            alert = new AlertForm(kind, pct);
            alert.Dismissed += (f, skip) =>
            {
                skipUntilResolved = skip;
                nextAlertAt = DateTime.Now.AddMinutes(Settings.RepeatMinutes);
            };
            alert.FormClosed += (s, e) => { if (ReferenceEquals(s, alert)) { alert = null; StopSound(); } };
            alert.Show();
            if (Settings.Sound) { sound.PlayLooping(kind); soundTimer.Stop(); soundTimer.Start(); }
        }

        void CloseAlert()
        {
            if (alert == null) return;
            var a = alert; alert = null;
            StopSound();
            a.Close();
        }

        void StopSound() { soundTimer.Stop(); sound.Stop(); }

        void ShowTestAlert()
        {
            CloseTestAlert();
            if (alert != null) return; // असली अलार्म पहले से दिख रहा है
            var b = BatterySnapshot.Read();
            var kind = b.PluggedIn ? AlertKind.High : AlertKind.Low;
            int pct = b.Percent >= 0 ? b.Percent : (kind == AlertKind.High ? Settings.High : Settings.Low);
            testAlert = new AlertForm(kind, pct);
            testAlert.FormClosed += (s, e) => { if (ReferenceEquals(s, testAlert)) { testAlert = null; StopSound(); } };
            testAlert.Show();
            if (Settings.Sound) { sound.PlayLooping(kind); soundTimer.Stop(); soundTimer.Start(); }
        }

        void CloseTestAlert()
        {
            if (testAlert == null) return;
            var t = testAlert; testAlert = null;
            StopSound();
            t.Close();
        }

        void TogglePause() => SetPaused(DateTime.Now >= pausedUntil);

        void SetPaused(bool paused)
        {
            if (paused)
            {
                pausedUntil = DateTime.Now.AddHours(1);
                CloseAlert();
            }
            else
            {
                pausedUntil = DateTime.MinValue;
                if (IsHandleCreated) BeginInvoke((Action)CheckBattery);
            }
            UpdatePauseText();
        }

        void UpdatePauseText()
        {
            bool paused = pausedUntil != DateTime.MinValue && DateTime.Now < pausedUntil;
            if (miPause != null)
                miPause.Text = paused ? Lang.T("अलार्म फिर से चालू करें", "Resume alarms")
                                      : Lang.T("अलार्म 1 घंटे के लिए रोकें", "Pause alarms for 1 hour");
            lblPause.Visible = paused;
            if (paused) lblPause.Text = Lang.T($"अलार्म {pausedUntil:hh:mm tt} तक रुके हुए हैं", $"Alarms paused until {pausedUntil:hh:mm tt}");
        }

        // =====================================================================
        //  UI अपडेट
        // =====================================================================
        void UpdateStatusUi(BatterySnapshot b)
        {
            if (!b.HasBattery)
            {
                lblPercent.Text = "--";
                lblPercent.ForeColor = Brand.Muted;
                lblStatus.Text = Lang.T("इस कंप्यूटर में बैटरी नहीं मिली", "No battery found on this computer");
                lblTime.Text = Lang.T("यह ऐप लैपटॉप के लिए बनाया गया है।", "This app is made for laptops.");
                bar.Value = 0; bar.FillColor = Brand.Muted; bar.Invalidate();
                return;
            }

            lblPercent.Text = b.Percent + "%";
            if (b.Charging) lblStatus.Text = Lang.T("चार्ज हो रहा है", "Charging");
            else if (b.PluggedIn) lblStatus.Text = Lang.T("चार्जर लगा है", "Plugged in");
            else lblStatus.Text = Lang.T("बैटरी पर चल रहा है", "On battery");

            if (!b.PluggedIn && b.SecondsLeft > 0)
            {
                int h = b.SecondsLeft / 3600, m = (b.SecondsLeft % 3600) / 60;
                lblTime.Text = h > 0 ? Lang.T($"लगभग {h} घंटे {m} मिनट बचे", $"About {h} hr {m} min left")
                                     : Lang.T($"लगभग {m} मिनट बचे", $"About {m} min left");
            }
            else if (b.PluggedIn && b.Percent >= Settings.High)
                lblTime.Text = Lang.T($"{Settings.High}% से ऊपर – चार्जर निकालने का समय", $"Above {Settings.High}% – time to unplug the charger");
            else
                lblTime.Text = Lang.T($"आपकी सेटिंग: {Settings.Low}% पर चार्जर लगाएँ, {Settings.High}% पर निकालें",
                                      $"Your setting: plug in at {Settings.Low}%, unplug at {Settings.High}%");

            bar.Value = b.Percent;
            bar.FillColor = ColorFor(b.Percent, b.PluggedIn);
            bar.Invalidate();
            lblPercent.ForeColor = bar.FillColor;
        }

        static Color ColorFor(int pct, bool plugged)
        {
            if (pct < 0) return Brand.Muted;
            if (pct <= Settings.Low) return Brand.Bad;
            if (pct >= Settings.High && plugged) return Brand.Good;
            if (pct <= Settings.Low + 15) return Brand.Warn;
            return Brand.Primary;
        }

        void UpdateTrayIcon(BatterySnapshot b)
        {
            if (tray == null) return;
            int pct = b.HasBattery ? b.Percent : -1;
            string tip = b.HasBattery
                ? $"{Brand.AppName}: {pct}% " + (b.Charging ? Lang.T("(चार्ज हो रहा है)", "(charging)")
                                                : b.PluggedIn ? Lang.T("(चार्जर लगा है)", "(plugged in)")
                                                              : Lang.T("(बैटरी पर)", "(on battery)"))
                : Brand.AppName + ": " + Lang.T("बैटरी नहीं मिली", "no battery");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (pct == iconPct && b.PluggedIn == iconPlugged) return;
            iconPct = pct; iconPlugged = b.PluggedIn;
            var old = trayIconImg;
            trayIconImg = TrayIconRenderer.Render(pct, b.PluggedIn, ColorFor(pct, b.PluggedIn));
            tray.Icon = trayIconImg;
            old?.Dispose();
        }

        void RefreshHealth()
        {
            lastHealth = null;
            RenderHealth(null);
            btnHealthRefresh.Enabled = false;
            Task.Run(() => BatteryHealth.Read()).ContinueWith(t =>
            {
                if (IsDisposed) return;
                btnHealthRefresh.Enabled = true;
                lastHealth = t.Status == TaskStatus.RanToCompletion ? t.Result : new HealthInfo();
                RenderHealth(lastHealth);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RenderHealth(HealthInfo h)
        {
            if (h == null)
            {
                lblHealth.Text = Lang.T("जाँच हो रही है…", "Checking…");
                lblHealth.ForeColor = Brand.Muted;
                lblHealthDetail.Text = "";
            }
            else if (h.Ok)
            {
                lblHealth.Text = Lang.T("बैटरी हेल्थ", "Battery health") + $": {h.Percent:0}%  –  {h.Verdict}";
                lblHealth.ForeColor = h.Percent >= 80 ? Brand.Good : h.Percent >= 60 ? Brand.Warn : Brand.Bad;
                lblHealthDetail.Text =
                    Lang.T("नई बैटरी की क्षमता", "Original (design) capacity") + $": {h.DesignCapacity:N0} mWh\n" +
                    Lang.T("अभी की पूरी क्षमता", "Current full capacity") + $": {h.FullChargeCapacity:N0} mWh" +
                    (h.CycleCount > 0 ? "\n" + Lang.T("चार्ज साइकल", "Charge cycles") + $": {h.CycleCount:N0}" : "");
            }
            else
            {
                lblHealth.Text = Lang.T("बैटरी हेल्थ: जानकारी नहीं मिल पाई", "Battery health: not available");
                lblHealth.ForeColor = Brand.Muted;
                lblHealthDetail.Text = Lang.T("कुछ लैपटॉप यह जानकारी नहीं देते। \"पूरी रिपोर्ट\" बटन आज़माएँ।",
                                              "Some laptops don't share this. Try the \"Full report\" button.");
            }
        }

        void OpenFullReport()
        {
            btnReport.Enabled = false;
            Cursor = Cursors.WaitCursor;
            Task.Run(() => BatteryHealth.CreateFullReport()).ContinueWith(t =>
            {
                if (IsDisposed) return;
                btnReport.Enabled = true;
                Cursor = Cursors.Default;
                string path = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (path != null) OpenUrl(path);
                else MessageBox.Show(this, Lang.T("Windows बैटरी रिपोर्ट नहीं बना पाया।", "Windows could not create the battery report."),
                                     Brand.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // =====================================================================
        //  विंडो / ट्रे व्यवहार
        // =====================================================================
        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow) value = false;
            base.SetVisibleCore(value);
        }

        void ShowFromTray()
        {
            allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                // बंद करने पर ऐप ट्रे में चलता रहता है
                e.Cancel = true;
                Hide();
                if (!Settings.TrayHintShown) ShowTrayHint();
                return;
            }
            base.OnFormClosing(e);
        }

        void ShowTrayHint()
        {
            tray.ShowBalloonTip(6000,
                Lang.T(Brand.AppName + " चालू है", Brand.AppName + " is running"),
                Lang.T("ऐप नीचे दाएँ कोने (ट्रे) में चलता रहेगा और बैटरी पर नज़र रखेगा। बंद करने के लिए आइकन पर राइट-क्लिक करें।",
                       "The app keeps running in the tray (bottom-right) and watches your battery. Right-click the icon to exit."),
                ToolTipIcon.Info);
            Settings.TrayHintShown = true;
            Settings.Save();
        }

        void ExitApp()
        {
            exiting = true;
            pollTimer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            CloseAlert(); CloseTestAlert();
            sound.Dispose();
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }

        protected override void WndProc(ref Message m)
        {
            if (NativeMethods.WM_SHOWME > 0 && m.Msg == NativeMethods.WM_SHOWME) { ShowFromTray(); return; }
            base.WndProc(ref m);
        }

        void ShowAbout()
        {
            string info = Lang.T(
                $"{Brand.AppName} v1.2 – मुफ़्त सॉफ्टवेयर\n\nयह ऐप {Brand.Company} ने बनाया है।\n{Brand.Tagline}।\n\n{Brand.Location}",
                $"{Brand.AppName} v1.2 – Free software\n\nThis app is made by {Brand.Company}.\n{Brand.TaglineEn}.\n\n{Brand.Location}");
            if (Brand.Website != "") info += "\n" + Lang.T("वेबसाइट", "Website") + ": " + Brand.Website;
            if (Brand.Phone != "") info += "\n" + Lang.T("फ़ोन", "Phone") + ": " + Brand.Phone;
            if (Brand.Email != "") info += "\n" + Lang.T("ईमेल", "Email") + ": " + Brand.Email;
            info += "\n\n" + Lang.T("क्या आपको भी अपने बिज़नेस के लिए ऐप, वेबसाइट या सॉफ्टवेयर चाहिए? हमसे संपर्क करें!",
                                    "Need an app, website or software for your business? Get in touch!");
            MessageBox.Show(Visible ? this : null, info, Lang.T(Brand.Company + " के बारे में", "About " + Brand.Company),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void TalkToUs()
        {
            if (Brand.WhatsApp != "")
                OpenUrl("https://wa.me/" + Brand.WhatsApp + "?text=" + Uri.EscapeDataString(Lang.T(Brand.WhatsAppMsg, Brand.WhatsAppMsgEn)));
            else if (Brand.Website != "")
                OpenUrl(Brand.Website);
            else
                ShowAbout();
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // =====================================================================
        //  UI बनाना
        // =====================================================================
        const int ContentWidth = 440;

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = $"{Brand.AppName} – {Brand.Company}";
            Font = new Font(Brand.FontName, 9.5f);
            BackColor = Brand.Surface;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                Padding = new Padding(0, 0, 0, 14), Margin = Padding.Empty,
            };

            // ---- हेडर (बाएँ नाम, दाएँ भाषा टॉगल) ----
            var header = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, BackColor = Brand.Primary,
                Width = ContentWidth + 62, Height = 74, Padding = new Padding(18, 12, 14, 10), Margin = new Padding(0, 0, 0, 6),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            titles.Controls.Add(new Label { Text = Brand.AppName, ForeColor = Color.White, AutoSize = true, Font = new Font(Brand.FontName, 16f, FontStyle.Bold), Margin = Padding.Empty });
            titles.Controls.Add(Loc(new Label { ForeColor = Color.FromArgb(210, 220, 255), AutoSize = true, Margin = Padding.Empty },
                "by " + Brand.Company + "  •  लैपटॉप बैटरी की उम्र बढ़ाएँ", "by " + Brand.Company + "  •  Make your laptop battery last longer"));
            header.Controls.Add(titles, 0, 0);

            var langBox = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 6, 0, 0) };
            btnHindi = LangButton("हिंदी");
            btnHindi.Click += (s, e) => SetLanguage("hi");
            btnEnglish = LangButton("English");
            btnEnglish.Click += (s, e) => SetLanguage("en");
            langBox.Controls.Add(btnHindi);
            langBox.Controls.Add(btnEnglish);
            header.Controls.Add(langBox, 1, 0);
            StyleLangButtons();
            root.Controls.Add(header);

            // ---- अभी की स्थिति ----
            var status = Card("अभी की स्थिति", "Current status");
            lblPercent = new Label { AutoSize = true, Font = new Font(Brand.FontName, 26f, FontStyle.Bold), ForeColor = Brand.Primary, Margin = new Padding(0, 0, 0, 2), Text = "--" };
            bar = new BatteryBar { Width = ContentWidth - 4, Height = 16, Margin = new Padding(2, 0, 0, 8) };
            lblStatus = new Label { AutoSize = true, Font = new Font(Brand.FontName, 10.5f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
            lblTime = new Label { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(ContentWidth, 0), Margin = Padding.Empty };
            lblPause = new Label { AutoSize = true, ForeColor = Brand.Warn, Visible = false, Margin = new Padding(0, 4, 0, 0) };
            AddRows(status, lblPercent, bar, lblStatus, lblTime, lblPause);
            root.Controls.Add(Wrap(status));

            // ---- बैटरी हेल्थ ----
            var health = Card("बैटरी हेल्थ", "Battery health");
            lblHealth = new Label { AutoSize = true, Font = new Font(Brand.FontName, 10.5f, FontStyle.Bold), MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 3) };
            lblHealthDetail = new Label { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 8) };
            var hb = ButtonRow();
            btnHealthRefresh = Loc(SmallButton(), "फिर से जाँचें", "Check again");
            btnHealthRefresh.Click += (s, e) => RefreshHealth();
            btnReport = Loc(SmallButton(), "पूरी रिपोर्ट खोलें (Windows)", "Open full report (Windows)");
            btnReport.Click += (s, e) => OpenFullReport();
            hb.Controls.Add(btnHealthRefresh); hb.Controls.Add(btnReport);
            AddRows(health, lblHealth, lblHealthDetail, hb);
            root.Controls.Add(Wrap(health));

            // ---- सेटिंग्स ----
            var set = Card("अलार्म सेटिंग्स", "Alarm settings");
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth - 90));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            numHigh = Num(50, 100, Settings.High);
            numLow = Num(5, 50, Settings.Low);
            numRepeat = Num(1, 60, Settings.RepeatMinutes);
            AddSetting(grid, "चार्जर निकालने का अलार्म, जब बैटरी इतने % हो:", "Unplug alarm when battery reaches (%):", numHigh);
            AddSetting(grid, "चार्जर लगाने का अलार्म, जब बैटरी इतने % रह जाए:", "Plug-in alarm when battery drops to (%):", numLow);
            AddSetting(grid, "अलार्म बंद करने के बाद दोबारा कितने मिनट में:", "Remind again after (minutes):", numRepeat);
            chkSound = Loc(new CheckBox { Checked = Settings.Sound, AutoSize = true, Margin = new Padding(0, 4, 0, 2) },
                           "अलार्म के साथ आवाज़ (टोन) बजाएँ", "Play a sound with the alarm");
            chkAuto = Loc(new CheckBox { Checked = Settings.AutoStart, AutoSize = true, Margin = new Padding(0, 2, 0, 8) },
                          "Windows चालू होते ही Battery Guard शुरू करें", "Start Battery Guard with Windows");
            var sb = ButtonRow();
            var btnTest = Loc(SmallButton(), "अलार्म टेस्ट करें", "Test alarm");
            btnTest.Click += (s, e) => ShowTestAlert();
            var btnDefault = Loc(SmallButton(), "डिफ़ॉल्ट (80% / 20%)", "Defaults (80% / 20%)");
            btnDefault.Click += (s, e) => { numHigh.Value = 80; numLow.Value = 20; numRepeat.Value = 5; };
            sb.Controls.Add(btnTest); sb.Controls.Add(btnDefault);
            var tip = Loc(new Label { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), ForeColor = Brand.Muted, Margin = new Padding(0, 8, 0, 0) },
                "सुझाव: लैपटॉप की बैटरी को 20% से 80% के बीच रखने से उसकी उम्र काफ़ी बढ़ जाती है। हमेशा 100% पर चार्जर लगाए रखना बैटरी को जल्दी खराब करता है।",
                "Tip: Keeping your laptop battery between 20% and 80% makes it last much longer. Staying plugged in at 100% all the time wears it out faster.");
            AddRows(set, grid, chkSound, chkAuto, sb, tip);
            root.Controls.Add(Wrap(set));

            numHigh.ValueChanged += (s, e) => { if (numLow.Value > numHigh.Value - 10) numLow.Value = Math.Max(numLow.Minimum, numHigh.Value - 10); SaveSettings(); };
            numLow.ValueChanged += (s, e) => { if (numHigh.Value < numLow.Value + 10) numHigh.Value = Math.Min(numHigh.Maximum, numLow.Value + 10); SaveSettings(); };
            numRepeat.ValueChanged += (s, e) => SaveSettings();
            chkSound.CheckedChanged += (s, e) => { SaveSettings(); if (!chkSound.Checked) StopSound(); };
            chkAuto.CheckedChanged += (s, e) => { SaveSettings(); AutoStartHelper.Apply(Settings.AutoStart); };

            // ---- "Made with ♥ in Andaman" (धड़कता दिल) ----
            var madeRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            var madeL = Loc(new Label { AutoSize = true, ForeColor = Brand.Muted, Font = new Font(Brand.FontName, 9.5f), Margin = new Padding(0, 3, 0, 0) }, "अंडमान में", "Made with");
            heart = new HeartControl { Size = new Size(24, 24), Margin = new Padding(2, 0, 2, 0) };
            var madeR = Loc(new Label { AutoSize = true, ForeColor = Brand.Muted, Font = new Font(Brand.FontName, 9.5f), Margin = new Padding(0, 3, 0, 0) }, "से बनाया गया", "in Andaman");
            madeRow.Controls.Add(madeL); madeRow.Controls.Add(heart); madeRow.Controls.Add(madeR);
            var madeWrap = new TableLayoutPanel { ColumnCount = 1, Width = ContentWidth + 62, Height = 34, Margin = new Padding(0, 8, 0, 0) };
            madeRow.Anchor = AnchorStyles.None;
            madeWrap.Controls.Add(madeRow);
            root.Controls.Add(madeWrap);
            VisibleChanged += (s, e) => heart.Animate = Visible;

            // ---- "Need software made for your business?" कार्ड ----
            var promo = new DashedPanel { AutoSize = true, Padding = new Padding(12), Margin = new Padding(16, 2, 16, 0), MinimumSize = new Size(ContentWidth + 30, 0), MaximumSize = new Size(ContentWidth + 30, 0) };
            var inner = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, BackColor = Color.White, Margin = Padding.Empty, Dock = DockStyle.Fill };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var bulb = new BulbIcon { Size = new Size(42, 42), Margin = new Padding(0, 0, 12, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, BackColor = Color.White };
            texts.Controls.Add(Loc(new Label { AutoSize = true, MaximumSize = new Size(200, 0), Font = new Font(Brand.FontName, 10f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 30, 45), Margin = new Padding(0, 0, 0, 3) },
                "अपने बिज़नेस के लिए सॉफ्टवेयर चाहिए?", "Need software made for your business?"));
            texts.Controls.Add(Loc(new Label { AutoSize = true, MaximumSize = new Size(200, 0), Font = new Font(Brand.FontName, 8.5f), ForeColor = Brand.Muted, Margin = Padding.Empty },
                "हम आपके बिज़नेस के लिए कस्टम सॉफ्टवेयर और ऐप बनाते हैं।", "We build custom software and apps for your business."));
            var talk = Loc(SmallButton(), $"{Brand.Company} से बात करें", $"Talk to {Brand.Company}");
            talk.Image = ChatIcon.Make();
            talk.TextImageRelation = TextImageRelation.ImageBeforeText;
            talk.ImageAlign = ContentAlignment.MiddleLeft;
            talk.ForeColor = Color.FromArgb(25, 30, 45);
            talk.Font = new Font(Brand.FontName, 9f, FontStyle.Bold);
            talk.Margin = new Padding(8, 6, 0, 0);
            talk.Anchor = AnchorStyles.Right;
            talk.Click += (s, e) => TalkToUs();
            inner.Controls.Add(bulb, 0, 0);
            inner.Controls.Add(texts, 1, 0);
            inner.Controls.Add(talk, 2, 0);
            promo.Controls.Add(inner);
            root.Controls.Add(promo);

            Controls.Add(root);
            ResumeLayout(true);
        }

        void SaveSettings()
        {
            Settings.High = (int)numHigh.Value;
            Settings.Low = (int)numLow.Value;
            Settings.RepeatMinutes = (int)numRepeat.Value;
            Settings.Sound = chkSound.Checked;
            Settings.AutoStart = chkAuto.Checked;
            Settings.Save();
            nextAlertAt = DateTime.MinValue;
            if (IsHandleCreated) BeginInvoke((Action)CheckBattery);
        }

        // ---- छोटे UI हेल्पर ----
        TableLayoutPanel Card(string hi, string en)
        {
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.White, Padding = new Padding(14, 10, 14, 12), Margin = Padding.Empty };
            t.Controls.Add(Loc(new Label { AutoSize = true, ForeColor = Brand.Primary, Font = new Font(Brand.FontName, 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, hi, en));
            return t;
        }

        static Control Wrap(Control card)
        {
            // कार्ड के चारों ओर हल्का बॉर्डर
            var p = new Panel { AutoSize = true, BackColor = Color.FromArgb(222, 227, 236), Padding = new Padding(1), Margin = new Padding(16, 6, 16, 4) };
            card.MinimumSize = new Size(ContentWidth + 28, 0);
            p.Controls.Add(card);
            return p;
        }

        static void AddRows(TableLayoutPanel t, params Control[] cs) { foreach (var c in cs) t.Controls.Add(c); }

        static FlowLayoutPanel ButtonRow() => new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };

        static Button SmallButton()
        {
            var b = new Button { AutoSize = true, Padding = new Padding(6, 1, 6, 1), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand, BackColor = Color.White, FlatStyle = FlatStyle.Flat, ForeColor = Brand.PrimaryDk, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Color.FromArgb(190, 198, 215);
            return b;
        }

        static Button BrandButton(Color bg)
        {
            var b = new Button { AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand, BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Brand.FontName, 9.5f, FontStyle.Bold), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        static Button LangButton(string text)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(62, 0), Padding = new Padding(6, 1, 6, 1), Margin = new Padding(0), Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        static NumericUpDown Num(int min, int max, int val)
            => new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = 64, TextAlign = HorizontalAlignment.Center, Margin = new Padding(6, 3, 0, 3) };

        void AddSetting(TableLayoutPanel g, string hi, string en, Control input)
        {
            g.Controls.Add(Loc(new Label { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(ContentWidth - 96, 0), Margin = new Padding(0, 6, 0, 6) }, hi, en));
            g.Controls.Add(input);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                pollTimer?.Dispose(); soundTimer?.Dispose(); sound.Dispose();
                tray?.Dispose(); trayIconImg?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>बैटरी का रंगीन बार।</summary>
    sealed class BatteryBar : Control
    {
        public int Value;
        public Color FillColor = Brand.Primary;

        public BatteryBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var bg = TrayIconRenderer.Rounded(r, Height / 2f))
            using (var br = new SolidBrush(Color.FromArgb(232, 235, 242)))
                g.FillPath(br, bg);
            float w = Math.Max(0, Math.Min(100, Value)) / 100f * r.Width;
            if (w >= Height / 2f)
            {
                using (var fp = TrayIconRenderer.Rounded(new RectangleF(r.X, r.Y, w, r.Height), Height / 2f))
                using (var br = new SolidBrush(FillColor))
                    g.FillPath(br, fp);
            }
        }
    }

    static class TrayIconRenderer
    {
        /// <summary>ट्रे में बैटरी % वाला आइकन बनाता है।</summary>
        public static Icon Render(int pct, bool plugged, Color color)
        {
            Size sz = SystemInformation.SmallIconSize;
            using (var bmp = new Bitmap(sz.Width, sz.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float W = sz.Width, H = sz.Height;
                using (var path = Rounded(new RectangleF(0, 0, W - 1, H - 1), W / 5f))
                using (var br = new SolidBrush(color))
                    g.FillPath(br, path);

                if (plugged)
                    using (var y = new SolidBrush(Color.FromArgb(255, 214, 10)))
                        g.FillRectangle(y, W * 0.2f, H - Math.Max(2, H / 8f) - 1, W * 0.6f, Math.Max(2, H / 8f));

                string text = pct < 0 ? "?" : pct.ToString();
                float size = H * 0.62f;
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    Font font = null;
                    try
                    {
                        while (true)
                        {
                            font?.Dispose();
                            font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                            if (g.MeasureString(text, font, 1000, StringFormat.GenericTypographic).Width <= W - 1 || size <= 6) break;
                            size -= 0.5f;
                        }
                        using (var white = new SolidBrush(Color.White))
                            g.DrawString(text, font, white, new RectangleF(0, plugged ? -1 : 0, W, H), sf);
                    }
                    finally { font?.Dispose(); }
                }

                IntPtr h = bmp.GetHicon();
                try { using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone(); }
                finally { NativeMethods.SafeDestroyIcon(h); }
            }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>धड़कता हुआ दिल ("Made with ♥ in Andaman")। सिर्फ़ विंडो दिखने पर चलता है।</summary>
    sealed class HeartControl : Control
    {
        readonly Timer timer = new Timer { Interval = 30 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        float scale = 1f;

        public HeartControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            timer.Tick += (s, e) => { scale = BeatScale(clock.ElapsedMilliseconds); Invalidate(); };
        }

        public bool Animate
        {
            get => timer.Enabled;
            set { if (value) { clock.Restart(); timer.Start(); } else { timer.Stop(); scale = 1f; Invalidate(); } }
        }

        // दिल की धड़कन: "लब-डब" (दो बार बड़ा), फिर विराम। एक चक्र 1.3 सेकंड।
        static float BeatScale(long ms)
        {
            double t = (ms % 1300) / 1300.0;
            double Bump(double center, double width, double height)
            {
                double x = (t - center) / width;
                return Math.Abs(x) >= 1 ? 0 : height * (0.5 + 0.5 * Math.Cos(Math.PI * x));
            }
            return (float)(1.0 + Bump(0.10, 0.10, 0.28) + Bump(0.32, 0.10, 0.18));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(Width, Height) * 0.66f * scale;
            float cx = Width / 2f, cy = Height / 2f + s * 0.04f;
            using (var path = HeartPath(cx, cy, s))
            using (var br = new LinearGradientBrush(new RectangleF(cx - s / 2, cy - s / 2, s, s + 1), Color.FromArgb(255, 82, 102), Color.FromArgb(220, 30, 60), 90f))
                g.FillPath(br, path);
        }

        static GraphicsPath HeartPath(float cx, float cy, float size)
        {
            float w = size, h = size * 0.92f;
            float x = cx - w / 2, y = cy - h / 2;
            var p = new GraphicsPath();
            p.StartFigure();
            p.AddBezier(cx, y + h * 0.28f, cx, y, x, y, x, y + h * 0.32f);
            p.AddBezier(x, y + h * 0.32f, x, y + h * 0.62f, cx - w * 0.10f, y + h * 0.80f, cx, y + h);
            p.AddBezier(cx, y + h, cx + w * 0.10f, y + h * 0.80f, x + w, y + h * 0.62f, x + w, y + h * 0.32f);
            p.AddBezier(x + w, y + h * 0.32f, x + w, y, cx, y, cx, y + h * 0.28f);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>गोल कोनों और डैश बॉर्डर वाला सफ़ेद कार्ड।</summary>
    sealed class DashedPanel : Panel
    {
        public DashedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Brand.Surface);
            var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            using (var path = TrayIconRenderer.Rounded(r, 10))
            {
                using (var br = new SolidBrush(Color.White)) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(205, 210, 222), 1.2f) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>गुलाबी डिब्बे में बल्ब (आइडिया) का आइकन।</summary>
    sealed class BulbIcon : Control
    {
        public BulbIcon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);
            float W = Width, H = Height;
            using (var box = TrayIconRenderer.Rounded(new RectangleF(0, 0, W - 1, H - 1), W * 0.22f))
            using (var pink = new SolidBrush(Color.FromArgb(255, 228, 230)))
                g.FillPath(pink, box);

            float d = W * 0.42f, bx = (W - d) / 2, by = H * 0.17f;
            using (var glass = new SolidBrush(Color.FromArgb(250, 204, 21)))
            {
                g.FillEllipse(glass, bx, by, d, d);
                g.FillRectangle(glass, W / 2 - d * 0.22f, by + d * 0.75f, d * 0.44f, d * 0.28f);
            }
            using (var shine = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
                g.FillEllipse(shine, bx + d * 0.2f, by + d * 0.15f, d * 0.25f, d * 0.25f);
            using (var metal = new SolidBrush(Color.FromArgb(120, 128, 145)))
            {
                g.FillRectangle(metal, W / 2 - d * 0.24f, by + d * 1.05f, d * 0.48f, H * 0.06f);
                g.FillRectangle(metal, W / 2 - d * 0.18f, by + d * 1.05f + H * 0.08f, d * 0.36f, H * 0.05f);
            }
        }
    }

    static class ChatIcon
    {
        /// <summary>बटन के लिए छोटा चैट-बबल आइकन।</summary>
        public static Bitmap Make()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.FromArgb(110, 118, 135), 1.3f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var p = TrayIconRenderer.Rounded(new RectangleF(1.5f, 2.5f, 13f, 9f), 4f)) g.DrawPath(pen, p);
                g.DrawLine(pen, 5f, 11.5f, 4f, 14.5f);
                g.DrawLine(pen, 4f, 14.5f, 8f, 11.5f);
            }
            return bmp;
        }
    }
}
