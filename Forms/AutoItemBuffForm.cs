using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    public class AutoItemBuffForm : Form, IObserver
    {
        private readonly Button btnToggle = new Button();
        private readonly TextBox txtAgiItemKey = new TextBox();
        private readonly TextBox txtSecondaryItemKey = new TextBox();
        private readonly NumericUpDown numCheckInterval = new NumericUpDown();
        private readonly TextBox txtLog = new TextBox();
        private readonly Button btnClearLog = new Button();

        private AutoItemBuff autoItemBuff;
        private bool appIsOn;

        public AutoItemBuffForm(Subject subject)
        {
            subject.Attach(this);
            InitializeComponent();
            WireEvents();
            AutoItemBuff.ItemBuffLogged += OnItemBuffLogged;
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    this.autoItemBuff = ProfileSingleton.GetCurrent().AutoItemBuff;
                    SyncControlsFromProfile();
                    LoadLogFile();
                    break;
                case MessageCode.TURN_OFF:
                    appIsOn = false;
                    this.autoItemBuff?.Stop();
                    break;
                case MessageCode.TURN_ON:
                    appIsOn = true;
                    if (this.autoItemBuff != null && this.autoItemBuff.isEnabled)
                    {
                        this.autoItemBuff.Start();
                    }
                    break;
            }
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.White;
            this.ClientSize = new Size(300, 274);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Text = "AutoItemBuffForm";

            btnToggle.Location = new Point(18, 12);
            btnToggle.Size = new Size(64, 24);
            btnToggle.Text = "OFF";
            btnToggle.BackColor = Color.Red;
            btnToggle.UseVisualStyleBackColor = false;

            Label lblAgiItem = new Label { Text = "AGI Item Key", Location = new Point(16, 52), AutoSize = true };
            txtAgiItemKey.Location = new Point(100, 48);
            txtAgiItemKey.Size = new Size(80, 23);
            txtAgiItemKey.Font = new Font("Microsoft Sans Serif", 10F);

            Label lblSecondaryItem = new Label { Text = "Secondary", Location = new Point(26, 90), AutoSize = true };
            txtSecondaryItemKey.Location = new Point(100, 86);
            txtSecondaryItemKey.Size = new Size(80, 23);
            txtSecondaryItemKey.Font = new Font("Microsoft Sans Serif", 10F);

            Label lblCheck = new Label { Text = "Check", Location = new Point(52, 128), AutoSize = true };
            numCheckInterval.Location = new Point(100, 124);
            numCheckInterval.Size = new Size(80, 23);
            numCheckInterval.Font = new Font("Microsoft Sans Serif", 10F);
            numCheckInterval.Minimum = 40;
            numCheckInterval.Maximum = 2000;
            Label lblCheckMs = new Label { Text = "ms", Location = new Point(186, 128), AutoSize = true };

            btnClearLog.Location = new Point(210, 12);
            btnClearLog.Size = new Size(68, 24);
            btnClearLog.Text = "Clear Log";
            btnClearLog.UseVisualStyleBackColor = true;

            txtLog.Location = new Point(18, 162);
            txtLog.Size = new Size(260, 102);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8F);

            this.Controls.Add(btnToggle);
            this.Controls.Add(lblAgiItem);
            this.Controls.Add(txtAgiItemKey);
            this.Controls.Add(lblSecondaryItem);
            this.Controls.Add(txtSecondaryItemKey);
            this.Controls.Add(lblCheck);
            this.Controls.Add(numCheckInterval);
            this.Controls.Add(lblCheckMs);
            this.Controls.Add(btnClearLog);
            this.Controls.Add(txtLog);
        }

        private void WireEvents()
        {
            btnToggle.Click += OnFeatureToggleClick;
            btnClearLog.Click += OnClearLogClick;

            txtAgiItemKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtAgiItemKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtAgiItemKey.TextChanged += OnAgiKeyChanged;

            txtSecondaryItemKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtSecondaryItemKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtSecondaryItemKey.TextChanged += OnSecondaryKeyChanged;

            numCheckInterval.ValueChanged += OnCheckChanged;
        }

        private void SyncControlsFromProfile()
        {
            if (autoItemBuff == null) return;

            txtAgiItemKey.Text = autoItemBuff.agiItemKey.ToString();
            txtSecondaryItemKey.Text = autoItemBuff.secondaryItemKey.ToString();
            numCheckInterval.Value = Math.Max((int)numCheckInterval.Minimum, Math.Min((int)numCheckInterval.Maximum, autoItemBuff.checkIntervalMs));
            UpdateToggleButton();
        }

        private void OnFeatureToggleClick(object sender, EventArgs e)
        {
            ToggleFeature();
        }

        private void ToggleFeature()
        {
            if (autoItemBuff == null) return;

            autoItemBuff.isEnabled = !autoItemBuff.isEnabled;
            ProfileSingleton.SetConfiguration(autoItemBuff);
            UpdateToggleButton();

            if (appIsOn)
            {
                if (autoItemBuff.isEnabled) autoItemBuff.Start();
                else autoItemBuff.Stop();
            }
        }

        private void UpdateToggleButton()
        {
            bool enabled = autoItemBuff != null && autoItemBuff.isEnabled;
            btnToggle.Text = enabled ? "ON" : "OFF";
            btnToggle.BackColor = enabled ? Color.Green : Color.Red;
        }

        private void OnAgiKeyChanged(object sender, EventArgs e)
        {
            if (autoItemBuff == null || string.IsNullOrWhiteSpace(txtAgiItemKey.Text)) return;
            try
            {
                autoItemBuff.agiItemKey = (Key)Enum.Parse(typeof(Key), txtAgiItemKey.Text);
                ProfileSingleton.SetConfiguration(autoItemBuff);
            }
            catch { }
        }

        private void OnSecondaryKeyChanged(object sender, EventArgs e)
        {
            if (autoItemBuff == null || string.IsNullOrWhiteSpace(txtSecondaryItemKey.Text)) return;
            try
            {
                autoItemBuff.secondaryItemKey = (Key)Enum.Parse(typeof(Key), txtSecondaryItemKey.Text);
                ProfileSingleton.SetConfiguration(autoItemBuff);
            }
            catch { }
        }

        private void OnCheckChanged(object sender, EventArgs e)
        {
            if (autoItemBuff == null) return;
            autoItemBuff.checkIntervalMs = (int)numCheckInterval.Value;
            ProfileSingleton.SetConfiguration(autoItemBuff);
        }

        private void OnItemBuffLogged(string line)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((MethodInvoker)delegate { AppendLogLine(line); });
        }

        private void OnClearLogClick(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "Clear Auto Item Buff logs for this profile?",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            txtLog.Clear();
            string path = AutoItemBuff.GetLogPath();
            try
            {
                if (File.Exists(path))
                {
                    File.WriteAllText(path, string.Empty);
                }
            }
            catch { }
        }

        private void LoadLogFile()
        {
            txtLog.Clear();
            string path = AutoItemBuff.GetLogPath();
            if (!File.Exists(path)) return;

            string[] recent = File.ReadAllLines(path).Reverse().Take(120).Reverse().ToArray();
            foreach (string line in recent)
            {
                AppendLogLine(line, false);
            }
        }

        private void AppendLogLine(string line, bool keepAtBottom = true)
        {
            txtLog.AppendText(line + Environment.NewLine);
            if (keepAtBottom)
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AutoItemBuff.ItemBuffLogged -= OnItemBuffLogged;
            }
            base.Dispose(disposing);
        }
    }
}
