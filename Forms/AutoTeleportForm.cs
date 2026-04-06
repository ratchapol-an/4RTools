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
    public class AutoTeleportForm : Form, IObserver
    {
        private readonly Button btnToggle = new Button();
        private readonly Button btnClearLog = new Button();
        private readonly TextBox txtToggleKey = new TextBox();
        private readonly TextBox txtFlyWingKey = new TextBox();
        private readonly NumericUpDown numHpPercent = new NumericUpDown();
        private readonly NumericUpDown numCheckInterval = new NumericUpDown();
        private readonly NumericUpDown numCooldown = new NumericUpDown();
        private readonly TextBox txtLog = new TextBox();

        private AutoTeleport autoTeleport;
        private bool appIsOn;
        private Keys lastToggleKey = Keys.None;

        public AutoTeleportForm(Subject subject)
        {
            subject.Attach(this);
            InitializeComponent();
            WireEvents();
            AutoTeleport.TeleportLogged += OnTeleportLogged;
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    UnregisterHotkey();
                    this.autoTeleport = ProfileSingleton.GetCurrent().AutoTeleport;
                    SyncControlsFromProfile();
                    RegisterHotkey();
                    LoadLogFile();
                    break;
                case MessageCode.TURN_OFF:
                    appIsOn = false;
                    this.autoTeleport?.Stop();
                    break;
                case MessageCode.TURN_ON:
                    appIsOn = true;
                    if (this.autoTeleport != null && this.autoTeleport.isEnabled)
                    {
                        this.autoTeleport.Start();
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
            this.Text = "AutoTeleportForm";

            btnToggle.Location = new Point(18, 12);
            btnToggle.Size = new Size(64, 24);
            btnToggle.Text = "OFF";
            btnToggle.BackColor = Color.Red;
            btnToggle.UseVisualStyleBackColor = false;

            Label lblToggleKey = new Label { Text = "Toggle", Location = new Point(90, 17), AutoSize = true };
            txtToggleKey.Location = new Point(136, 13);
            txtToggleKey.Size = new Size(80, 23);
            txtToggleKey.Font = new Font("Microsoft Sans Serif", 10F);

            Label lblFlyWing = new Label { Text = "Fly Wing", Location = new Point(24, 52), AutoSize = true };
            txtFlyWingKey.Location = new Point(87, 48);
            txtFlyWingKey.Size = new Size(80, 23);
            txtFlyWingKey.Font = new Font("Microsoft Sans Serif", 10F);

            Label lblHp = new Label { Text = "HP %", Location = new Point(186, 52), AutoSize = true };
            numHpPercent.Location = new Point(223, 48);
            numHpPercent.Size = new Size(55, 23);
            numHpPercent.Font = new Font("Microsoft Sans Serif", 10F);
            numHpPercent.Minimum = 1;
            numHpPercent.Maximum = 100;

            Label lblCheck = new Label { Text = "Check HP", Location = new Point(27, 90), AutoSize = true };
            numCheckInterval.Location = new Point(87, 86);
            numCheckInterval.Size = new Size(80, 23);
            numCheckInterval.Font = new Font("Microsoft Sans Serif", 10F);
            numCheckInterval.Minimum = 10;
            numCheckInterval.Maximum = 1000;

            Label lblCheckMs = new Label { Text = "ms", Location = new Point(173, 90), AutoSize = true };
            Label lblCooldown = new Label { Text = "Cooldown", Location = new Point(20, 128), AutoSize = true };
            numCooldown.Location = new Point(87, 124);
            numCooldown.Size = new Size(80, 23);
            numCooldown.Font = new Font("Microsoft Sans Serif", 10F);
            numCooldown.Minimum = 100;
            numCooldown.Maximum = 10000;
            Label lblCooldownMs = new Label { Text = "ms", Location = new Point(173, 128), AutoSize = true };

            txtLog.Location = new Point(18, 162);
            txtLog.Size = new Size(260, 102);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8F);

            btnClearLog.Location = new Point(218, 136);
            btnClearLog.Size = new Size(60, 22);
            btnClearLog.Text = "Clear Log";
            btnClearLog.UseVisualStyleBackColor = true;

            this.Controls.Add(btnToggle);
            this.Controls.Add(btnClearLog);
            this.Controls.Add(lblToggleKey);
            this.Controls.Add(txtToggleKey);
            this.Controls.Add(lblFlyWing);
            this.Controls.Add(txtFlyWingKey);
            this.Controls.Add(lblHp);
            this.Controls.Add(numHpPercent);
            this.Controls.Add(lblCheck);
            this.Controls.Add(numCheckInterval);
            this.Controls.Add(lblCheckMs);
            this.Controls.Add(lblCooldown);
            this.Controls.Add(numCooldown);
            this.Controls.Add(lblCooldownMs);
            this.Controls.Add(txtLog);
        }

        private void WireEvents()
        {
            btnToggle.Click += OnFeatureToggleClick;
            btnClearLog.Click += OnClearLogClick;

            txtToggleKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtToggleKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtToggleKey.TextChanged += OnToggleKeyChanged;

            txtFlyWingKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtFlyWingKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtFlyWingKey.TextChanged += OnFlyWingKeyChanged;

            numHpPercent.ValueChanged += OnHpPercentChanged;
            numCheckInterval.ValueChanged += OnCheckIntervalChanged;
            numCooldown.ValueChanged += OnCooldownChanged;
        }

        private void SyncControlsFromProfile()
        {
            if (autoTeleport == null)
            {
                return;
            }

            txtToggleKey.Text = autoTeleport.toggleKey;
            txtFlyWingKey.Text = autoTeleport.flyWingKey.ToString();
            numHpPercent.Value = Clamp(autoTeleport.hpPercent, (int)numHpPercent.Minimum, (int)numHpPercent.Maximum);
            numCheckInterval.Value = Clamp(autoTeleport.checkHpIntervalMs, (int)numCheckInterval.Minimum, (int)numCheckInterval.Maximum);
            numCooldown.Value = Clamp(autoTeleport.cooldownMs, (int)numCooldown.Minimum, (int)numCooldown.Maximum);
            UpdateToggleButton();
        }

        private void OnFeatureToggleClick(object sender, EventArgs e)
        {
            ToggleFeature();
        }

        private void ToggleFeature()
        {
            if (autoTeleport == null) return;

            autoTeleport.isEnabled = !autoTeleport.isEnabled;
            ProfileSingleton.SetConfiguration(autoTeleport);
            UpdateToggleButton();

            if (appIsOn)
            {
                if (autoTeleport.isEnabled) autoTeleport.Start();
                else autoTeleport.Stop();
            }
        }

        private void UpdateToggleButton()
        {
            bool enabled = autoTeleport != null && autoTeleport.isEnabled;
            btnToggle.Text = enabled ? "ON" : "OFF";
            btnToggle.BackColor = enabled ? Color.Green : Color.Red;
        }

        private void OnToggleKeyChanged(object sender, EventArgs e)
        {
            if (autoTeleport == null || string.IsNullOrWhiteSpace(txtToggleKey.Text))
            {
                return;
            }

            try
            {
                Keys parsed = (Keys)Enum.Parse(typeof(Keys), txtToggleKey.Text);
                autoTeleport.toggleKey = parsed.ToString();
                ProfileSingleton.SetConfiguration(autoTeleport);
                RegisterHotkey();
            }
            catch (Exception)
            {
                // Ignore temporary invalid key text.
            }
        }

        private void OnFlyWingKeyChanged(object sender, EventArgs e)
        {
            if (autoTeleport == null || string.IsNullOrWhiteSpace(txtFlyWingKey.Text))
            {
                return;
            }

            try
            {
                autoTeleport.flyWingKey = (Key)Enum.Parse(typeof(Key), txtFlyWingKey.Text);
                ProfileSingleton.SetConfiguration(autoTeleport);
            }
            catch (Exception)
            {
                // Ignore invalid key text while the user is editing.
            }
        }

        private void OnHpPercentChanged(object sender, EventArgs e)
        {
            if (autoTeleport == null) return;
            autoTeleport.hpPercent = (int)numHpPercent.Value;
            ProfileSingleton.SetConfiguration(autoTeleport);
        }

        private void OnCheckIntervalChanged(object sender, EventArgs e)
        {
            if (autoTeleport == null) return;
            autoTeleport.checkHpIntervalMs = (int)numCheckInterval.Value;
            ProfileSingleton.SetConfiguration(autoTeleport);
        }

        private void OnCooldownChanged(object sender, EventArgs e)
        {
            if (autoTeleport == null) return;
            autoTeleport.cooldownMs = (int)numCooldown.Value;
            ProfileSingleton.SetConfiguration(autoTeleport);
        }

        private decimal Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void RegisterHotkey()
        {
            if (autoTeleport == null || string.IsNullOrWhiteSpace(autoTeleport.toggleKey))
            {
                return;
            }

            UnregisterHotkey();
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), autoTeleport.toggleKey);
                if (KeyboardHook.Add(key, new KeyboardHook.KeyPressed(OnToggleHotkeyPressed)))
                {
                    lastToggleKey = key;
                }
            }
            catch (Exception)
            {
                lastToggleKey = Keys.None;
            }
        }

        private void UnregisterHotkey()
        {
            if (lastToggleKey != Keys.None)
            {
                KeyboardHook.Remove(lastToggleKey);
                lastToggleKey = Keys.None;
            }
        }

        private bool OnToggleHotkeyPressed()
        {
            this.BeginInvoke((MethodInvoker)delegate { ToggleFeature(); });
            return true;
        }

        private void OnTeleportLogged(string line)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((MethodInvoker)delegate
            {
                AppendLogLine(line);
            });
        }

        private void OnClearLogClick(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "Clear Auto Teleport logs for this profile?",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            txtLog.Clear();
            string path = GetLogPath();
            try
            {
                if (File.Exists(path))
                {
                    File.WriteAllText(path, string.Empty);
                }
            }
            catch (Exception)
            {
                // Keep UI responsive even if file operation fails.
            }
        }

        private void LoadLogFile()
        {
            txtLog.Clear();
            string path = GetLogPath();
            if (!File.Exists(path))
            {
                return;
            }

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

        private string GetLogPath()
        {
            string profileName = ProfileSingleton.GetCurrent()?.Name ?? "default";
            return Path.Combine(AppConfig.ProfileFolder, $"auto-teleport-{profileName}.log");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterHotkey();
                AutoTeleport.TeleportLogged -= OnTeleportLogged;
            }
            base.Dispose(disposing);
        }
    }
}
