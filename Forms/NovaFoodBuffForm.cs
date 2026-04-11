using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    public class NovaFoodBuffForm : Form, IObserver
    {
        private readonly Button btnToggle = new Button();
        private readonly TextBox txtBookKey = new TextBox();
        private readonly NumericUpDown numCheckInterval = new NumericUpDown();
        private readonly NumericUpDown numStepDelay = new NumericUpDown();
        private readonly Label lblStatus = new Label();
        private readonly TextBox txtLog = new TextBox();

        private NovaFoodBuff novaFoodBuff;
        private bool appIsOn;

        public NovaFoodBuffForm(Subject subject)
        {
            subject.Attach(this);
            InitializeComponent();
            WireEvents();
            NovaFoodBuff.NovaFoodLogged += OnNovaFoodLogged;
            NovaFoodBuff.NovaFoodToggled += OnNovaFoodToggled;
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    this.novaFoodBuff = ProfileSingleton.GetCurrent().NovaFoodBuff;
                    SyncControlsFromProfile();
                    break;
                case MessageCode.TURN_OFF:
                    appIsOn = false;
                    this.novaFoodBuff?.Stop();
                    break;
                case MessageCode.TURN_ON:
                    appIsOn = true;
                    if (this.novaFoodBuff != null && this.novaFoodBuff.isEnabled)
                    {
                        this.novaFoodBuff.Start();
                    }
                    break;
            }
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.White;
            this.ClientSize = new Size(320, 320);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Text = "NovaFoodBuffForm";

            btnToggle.Location = new Point(18, 12);
            btnToggle.Size = new Size(64, 24);
            btnToggle.Text = "OFF";
            btnToggle.BackColor = Color.Red;
            btnToggle.UseVisualStyleBackColor = false;

            Label lblKey = new Label { Text = "Manual Book Key", Location = new Point(16, 52), AutoSize = true };
            txtBookKey.Location = new Point(120, 48);
            txtBookKey.Size = new Size(80, 23);
            txtBookKey.Font = new Font("Microsoft Sans Serif", 10F);

            Label lblCheck = new Label { Text = "Check", Location = new Point(72, 86), AutoSize = true };
            numCheckInterval.Location = new Point(120, 82);
            numCheckInterval.Size = new Size(80, 23);
            numCheckInterval.Font = new Font("Microsoft Sans Serif", 10F);
            numCheckInterval.Minimum = 300;
            numCheckInterval.Maximum = 5000;
            Label lblCheckMs = new Label { Text = "ms", Location = new Point(206, 86), AutoSize = true };

            int rowY = 120;
            CreateDelayRow("Step Delay", numStepDelay, rowY); rowY += 34;

            lblStatus.Location = new Point(16, rowY);
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
            lblStatus.Text = "Status: waiting";

            txtLog.Location = new Point(18, rowY + 24);
            txtLog.Size = new Size(280, 140);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8F);

            this.Controls.Add(btnToggle);
            this.Controls.Add(lblKey);
            this.Controls.Add(txtBookKey);
            this.Controls.Add(lblCheck);
            this.Controls.Add(numCheckInterval);
            this.Controls.Add(lblCheckMs);
            this.Controls.Add(lblStatus);
            this.Controls.Add(txtLog);
        }

        private void CreateDelayRow(string label, NumericUpDown input, int y)
        {
            Label lbl = new Label { Text = label, Location = new Point(16, y + 4), AutoSize = true };
            input.Location = new Point(120, y);
            input.Size = new Size(80, 23);
            input.Font = new Font("Microsoft Sans Serif", 10F);
            input.Minimum = 0;
            input.Maximum = 3000;
            Label lblMs = new Label { Text = "ms", Location = new Point(206, y + 4), AutoSize = true };
            this.Controls.Add(lbl);
            this.Controls.Add(input);
            this.Controls.Add(lblMs);
        }

        private void WireEvents()
        {
            btnToggle.Click += (s, e) => ToggleFeature();
            txtBookKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtBookKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtBookKey.TextChanged += OnBookKeyChanged;
            numCheckInterval.ValueChanged += OnCheckChanged;
            numStepDelay.ValueChanged += (s, e) => UpdateDelayValuesFromControls();
        }

        private void SyncControlsFromProfile()
        {
            if (novaFoodBuff == null) return;

            txtBookKey.Text = novaFoodBuff.manualBookItemKey.ToString();
            numCheckInterval.Value = Math.Max((int)numCheckInterval.Minimum, Math.Min((int)numCheckInterval.Maximum, novaFoodBuff.checkIntervalMs));
            numStepDelay.Value = ClampToControl(numStepDelay, novaFoodBuff.stepDelayMs);
            UpdateToggleButton();
        }

        private decimal ClampToControl(NumericUpDown control, int value)
        {
            int min = (int)control.Minimum;
            int max = (int)control.Maximum;
            return Math.Max(min, Math.Min(max, value));
        }

        private void ToggleFeature()
        {
            if (novaFoodBuff == null) return;

            novaFoodBuff.isEnabled = !novaFoodBuff.isEnabled;
            ProfileSingleton.SetConfiguration(novaFoodBuff);
            UpdateToggleButton();

            if (appIsOn)
            {
                if (novaFoodBuff.isEnabled) novaFoodBuff.Start();
                else novaFoodBuff.Stop();
            }
        }

        private void UpdateToggleButton()
        {
            bool enabled = novaFoodBuff != null && novaFoodBuff.isEnabled;
            btnToggle.Text = enabled ? "ON" : "OFF";
            btnToggle.BackColor = enabled ? Color.Green : Color.Red;
            lblStatus.Text = enabled ? "Status: monitoring buff 273" : "Status: disabled";
            lblStatus.ForeColor = enabled ? Color.ForestGreen : Color.Firebrick;
        }

        private void OnBookKeyChanged(object sender, EventArgs e)
        {
            if (novaFoodBuff == null || string.IsNullOrWhiteSpace(txtBookKey.Text)) return;
            try
            {
                novaFoodBuff.manualBookItemKey = (Key)Enum.Parse(typeof(Key), txtBookKey.Text);
                ProfileSingleton.SetConfiguration(novaFoodBuff);
            }
            catch { }
        }

        private void OnCheckChanged(object sender, EventArgs e)
        {
            if (novaFoodBuff == null) return;
            novaFoodBuff.checkIntervalMs = (int)numCheckInterval.Value;
            ProfileSingleton.SetConfiguration(novaFoodBuff);
        }

        private void UpdateDelayValuesFromControls()
        {
            if (novaFoodBuff == null) return;
            novaFoodBuff.stepDelayMs = (int)numStepDelay.Value;
            ProfileSingleton.SetConfiguration(novaFoodBuff);
        }

        private void OnNovaFoodLogged(string line)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((MethodInvoker)delegate
            {
                txtLog.AppendText(line + Environment.NewLine);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            });
        }

        private void OnNovaFoodToggled(bool enabled)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (novaFoodBuff != null)
                {
                    novaFoodBuff.isEnabled = enabled;
                }
                UpdateToggleButton();
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NovaFoodBuff.NovaFoodLogged -= OnNovaFoodLogged;
                NovaFoodBuff.NovaFoodToggled -= OnNovaFoodToggled;
            }
            base.Dispose(disposing);
        }
    }
}
