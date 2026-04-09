using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Utils;
using _4RTools.Model;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace _4RTools.Forms
{
    public partial class StuffAutoBuffForm : Form, IObserver
    {
        private List<BuffContainer> stuffContainers = new List<BuffContainer>();
        private TabControl buffsTabControl;
        private TabPage tabIncreaseStrengthStatus;
        private TabPage tabAllBuffs;
        private GroupBox increaseStrengthGP;
        private Label lblIncreaseStrengthValue;
        private ListBox lstActiveBuffs;
        private Label lblActiveBuffsTitle;
        private Timer increaseStrengthStatusTimer;

        public StuffAutoBuffForm(Subject subject)
        {
            InitializeComponent();
            InitializeBuffTabs();
            stuffContainers.Add(new BuffContainer(this.PotionsGP, Buff.GetPotionsBuffs()));
            stuffContainers.Add(new BuffContainer(this.ElementalsGP, Buff.GetElementalsBuffs()));
            stuffContainers.Add(new BuffContainer(this.BoxesGP, Buff.GetBoxesBuffs()));
            stuffContainers.Add(new BuffContainer(this.FoodsGP, Buff.GetFoodBuffs()));
            stuffContainers.Add(new BuffContainer(this.ScrollBuffsGP, Buff.GetScrollBuffs()));
            stuffContainers.Add(new BuffContainer(this.EtcGP, Buff.GetETCBuffs()));
            stuffContainers.Add(new BuffContainer(this.CandiesGP, Buff.GetCandiesBuffs()));
            stuffContainers.Add(new BuffContainer(this.ExpGP, Buff.GetEXPBuffs()));

            new BuffRenderer("Autobuff", stuffContainers, toolTip1).doRender();
            InitializeIncreaseStrengthStatusTimer();
            UpdateIncreaseStrengthStatusLabel();
            this.FormClosed += StuffAutoBuffForm_FormClosed;

            subject.Attach(this);
        }

        private void InitializeBuffTabs()
        {
            this.buffsTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "buffsTabControl"
            };

            this.tabIncreaseStrengthStatus = new TabPage
            {
                Name = "tabIncreaseStrengthStatus",
                Text = "Increase STR",
                AutoScroll = true
            };

            this.tabAllBuffs = new TabPage
            {
                Name = "tabAllBuffs",
                Text = "All Buffs",
                AutoScroll = true
            };

            this.increaseStrengthGP = new GroupBox
            {
                Name = "IncreaseStrengthGP",
                Text = "Increase Strength Status",
                AutoSize = false,
                Location = new System.Drawing.Point(10, 12),
                Size = new System.Drawing.Size(520, 62),
                TabStop = false
            };

            this.lblIncreaseStrengthValue = new Label
            {
                Name = "lblIncreaseStrengthValue",
                AutoSize = true,
                Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold),
                Location = new Point(12, 28),
                Text = "Checking..."
            };

            this.lblActiveBuffsTitle = new Label
            {
                Name = "lblActiveBuffsTitle",
                AutoSize = true,
                Location = new Point(12, 82),
                Text = "Active Buffs (name / id):"
            };

            this.lstActiveBuffs = new ListBox
            {
                Name = "lstActiveBuffs",
                Location = new Point(12, 100),
                Size = new Size(492, 199)
            };

            this.increaseStrengthGP.Controls.Add(this.lblIncreaseStrengthValue);
            this.tabIncreaseStrengthStatus.Controls.Add(this.lblActiveBuffsTitle);
            this.tabIncreaseStrengthStatus.Controls.Add(this.lstActiveBuffs);
            this.tabIncreaseStrengthStatus.Controls.Add(this.increaseStrengthGP);
            this.buffsTabControl.TabPages.Add(this.tabIncreaseStrengthStatus);
            this.buffsTabControl.TabPages.Add(this.tabAllBuffs);

            this.Controls.Remove(this.PotionsGP);
            this.Controls.Remove(this.ElementalsGP);
            this.Controls.Remove(this.BoxesGP);
            this.Controls.Remove(this.FoodsGP);
            this.Controls.Remove(this.ScrollBuffsGP);
            this.Controls.Remove(this.EtcGP);
            this.Controls.Remove(this.CandiesGP);
            this.Controls.Remove(this.ExpGP);

            this.tabAllBuffs.Controls.Add(this.PotionsGP);
            this.tabAllBuffs.Controls.Add(this.ElementalsGP);
            this.tabAllBuffs.Controls.Add(this.BoxesGP);
            this.tabAllBuffs.Controls.Add(this.FoodsGP);
            this.tabAllBuffs.Controls.Add(this.ScrollBuffsGP);
            this.tabAllBuffs.Controls.Add(this.EtcGP);
            this.tabAllBuffs.Controls.Add(this.CandiesGP);
            this.tabAllBuffs.Controls.Add(this.ExpGP);

            this.Controls.Add(this.buffsTabControl);
            this.buffsTabControl.BringToFront();
        }

        private void InitializeIncreaseStrengthStatusTimer()
        {
            this.increaseStrengthStatusTimer = new Timer();
            this.increaseStrengthStatusTimer.Interval = 350;
            this.increaseStrengthStatusTimer.Tick += (s, e) => UpdateIncreaseStrengthStatusLabel();
            this.increaseStrengthStatusTimer.Start();
        }

        private void UpdateIncreaseStrengthStatusLabel()
        {
            bool hasBuff = HasIncreaseStrengthBuff();
            this.lblIncreaseStrengthValue.Text = hasBuff ? "Active" : "Inactive";
            this.lblIncreaseStrengthValue.ForeColor = hasBuff ? Color.ForestGreen : Color.Firebrick;
            UpdateActiveBuffsList();
        }

        private bool HasIncreaseStrengthBuff()
        {
            Client client = ClientSingleton.GetClient();
            if (client == null)
            {
                return false;
            }

            for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
            {
                EffectStatusIDs status = (EffectStatusIDs)client.CurrentBuffStatusCode(i);
                if (status == EffectStatusIDs.STR_3RD_FOOD)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateActiveBuffsList()
        {
            if (this.lstActiveBuffs == null)
            {
                return;
            }

            this.lstActiveBuffs.BeginUpdate();
            this.lstActiveBuffs.Items.Clear();

            Client client = ClientSingleton.GetClient();
            if (client == null)
            {
                this.lstActiveBuffs.Items.Add("No client selected.");
                this.lstActiveBuffs.EndUpdate();
                return;
            }

            HashSet<uint> shownStatuses = new HashSet<uint>();
            for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
            {
                uint currentStatus = client.CurrentBuffStatusCode(i);
                if (currentStatus == (uint)EffectStatusIDs.IGNORE || currentStatus == (uint)EffectStatusIDs.IGNORE_2)
                {
                    continue;
                }

                if (shownStatuses.Contains(currentStatus))
                {
                    continue;
                }

                shownStatuses.Add(currentStatus);
                EffectStatusIDs statusEnum = (EffectStatusIDs)currentStatus;
                string statusName = Enum.IsDefined(typeof(EffectStatusIDs), statusEnum)
                    ? statusEnum.ToString()
                    : "UNKNOWN_STATUS";

                this.lstActiveBuffs.Items.Add($"{statusName} ({currentStatus})");
            }

            if (this.lstActiveBuffs.Items.Count == 0)
            {
                this.lstActiveBuffs.Items.Add("No active buffs detected.");
            }

            this.lstActiveBuffs.EndUpdate();
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    BuffRenderer.doUpdate(new Dictionary<EffectStatusIDs, Key>(ProfileSingleton.GetCurrent().Autobuff.buffMapping), this);
                    UpdateIncreaseStrengthStatusLabel();
                    break;
                case MessageCode.TURN_OFF:
                    ProfileSingleton.GetCurrent().Autobuff.Stop();
                    UpdateIncreaseStrengthStatusLabel();
                    break;
                case MessageCode.TURN_ON:
                    ProfileSingleton.GetCurrent().Autobuff.Start();
                    UpdateIncreaseStrengthStatusLabel();
                    break;
            }
        }

        private void StuffAutoBuffForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (this.increaseStrengthStatusTimer != null)
            {
                this.increaseStrengthStatusTimer.Stop();
                this.increaseStrengthStatusTimer.Dispose();
                this.increaseStrengthStatusTimer = null;
            }
        }
    }
}