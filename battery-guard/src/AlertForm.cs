using System;
using System.Drawing;
using System.Windows.Forms;

namespace DigitalArts.BatteryGuard
{
    /// <summary>स्क्रीन के नीचे-दाएँ कोने में आने वाला पॉपअप। टाइपिंग में दखल नहीं देता (फ़ोकस नहीं छीनता)।</summary>
    sealed class AlertForm : Form
    {
        public AlertKind Kind { get; }
        /// <summary>true = "इस बार याद न दिलाएँ" (जब तक स्थिति न बदले)</summary>
        public event Action<AlertForm, bool> Dismissed;

        readonly Label title, body;
        readonly Button okBtn, skipBtn;
        int lastPercent;
        readonly Color accent;
        bool raised;

        public AlertForm(AlertKind kind, int percent)
        {
            Kind = kind;
            accent = kind == AlertKind.High ? Brand.Good : Brand.Bad;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(1);
            Font = new Font(Brand.FontName, 10f);
            Text = Brand.AppName;

            var root = new TableLayoutPanel
            {
                AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill,
                BackColor = Color.White, Margin = Padding.Empty, Padding = Padding.Empty,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var strip = new Panel { BackColor = accent, Dock = DockStyle.Fill, Margin = Padding.Empty };
            root.Controls.Add(strip, 0, 0);

            var col = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                Padding = new Padding(16, 14, 18, 12), Margin = Padding.Empty,
            };

            var head = new Label
            {
                AutoSize = true, ForeColor = Brand.Muted, Font = new Font(Brand.FontName, 8.5f),
                Text = Brand.AppName + "  ·  " + Brand.Company, Margin = new Padding(0, 0, 0, 4),
            };
            title = new Label
            {
                AutoSize = true, ForeColor = accent, Font = new Font(Brand.FontName, 15f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
            };
            body = new Label
            {
                AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.FromArgb(40, 40, 50),
                Margin = new Padding(0, 0, 0, 12),
            };

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, WrapContents = false };
            okBtn = MakeButton("OK", accent, Color.White);
            okBtn.Click += (s, e) => Dismiss(false);
            skipBtn = MakeButton("", Color.FromArgb(235, 238, 244), Color.FromArgb(50, 50, 60));
            skipBtn.Click += (s, e) => Dismiss(true);
            buttons.Controls.Add(okBtn);
            buttons.Controls.Add(skipBtn);

            col.Controls.Add(head);
            col.Controls.Add(title);
            col.Controls.Add(body);
            col.Controls.Add(buttons);
            root.Controls.Add(col, 1, 0);
            Controls.Add(root);

            UpdatePercent(percent);
            ResumeLayout(true);
        }

        static Button MakeButton(string text, Color bg, Color fg)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, BackColor = bg, ForeColor = fg, FlatStyle = FlatStyle.Flat,
                Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        public void UpdatePercent(int percent)
        {
            lastPercent = percent;
            if (Kind == AlertKind.High)
            {
                title.Text = Lang.T("चार्जर निकाल दें", "Unplug the charger");
                body.Text = Lang.T($"बैटरी {percent}% हो गई है। बैटरी की उम्र बढ़ाने के लिए अब चार्जर हटा दें।",
                                   $"Battery is at {percent}%. Unplug the charger now to protect battery health.");
            }
            else
            {
                title.Text = Lang.T("चार्जर लगाएँ", "Plug in the charger");
                body.Text = Lang.T($"बैटरी सिर्फ़ {percent}% बची है। बैटरी को बहुत नीचे जाने से बचाने के लिए अब चार्जर लगा दें।",
                                   $"Battery is down to {percent}%. Plug in the charger now to avoid deep discharge.");
            }
            okBtn.Text = Lang.T("ठीक है", "OK");
            skipBtn.Text = Lang.T("इस बार याद न दिलाएँ", "Don't remind this time");
        }

        /// <summary>भाषा बदलने पर टेक्स्ट दोबारा लिखें।</summary>
        public void ApplyLanguage()
        {
            UpdatePercent(lastPercent);
            PerformLayout();
            Reposition();
            if (IsHandleCreated) BeginInvoke((Action)Reposition); // लेआउट पूरा होने के बाद दोबारा सही जगह
        }

        void Reposition()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated) Reposition();
        }

        void Dismiss(bool skipUntilResolved)
        {
            if (raised) return;
            raised = true;
            Dismissed?.Invoke(this, skipUntilResolved);
            Close();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Reposition();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(200, 205, 215)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080;
                const int CS_DROPSHADOW = 0x00020000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }
    }
}
