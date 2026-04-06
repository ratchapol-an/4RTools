using _4RTools.Model;
using _4RTools.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Forms
{
    public class RemoteEndpointsForm : Form, IObserver
    {
        private readonly Subject subject;
        private readonly DataGridView endpointsGrid;
        private readonly Label lblStatus;
        private readonly Button btnRefresh;
        private readonly Timer refreshTimer;
        private readonly Dictionary<string, EndpointLearnRecord> learnedEndpoints = new Dictionary<string, EndpointLearnRecord>();
        private int currentPid = 0;

        private sealed class EndpointLearnRecord
        {
            public string RemoteIP { get; set; }
            public int RemotePort { get; set; }
            public string LastState { get; set; }
            public DateTime FirstSeenUtc { get; set; }
            public DateTime LastSeenUtc { get; set; }
            public int SeenCount { get; set; }
        }

        public RemoteEndpointsForm(Subject subject)
        {
            this.subject = subject;
            this.subject.Attach(this);

            this.BackColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Text = "RemoteEndpointsForm";
            this.ClientSize = new Size(860, 274);

            lblStatus = new Label
            {
                AutoSize = true,
                Location = new Point(12, 12),
                Text = "Select a Ragnarok client to start endpoint learning."
            };
            this.Controls.Add(lblStatus);

            btnRefresh = new Button
            {
                Text = "Refresh now",
                Location = new Point(730, 7),
                Size = new Size(110, 26)
            };
            btnRefresh.Click += (sender, e) => RefreshEndpointLearning();
            this.Controls.Add(btnRefresh);

            endpointsGrid = new DataGridView
            {
                Location = new Point(12, 40),
                Size = new Size(836, 222),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };
            endpointsGrid.Columns.Add("RemoteIP", "Remote IP");
            endpointsGrid.Columns.Add("RemotePort", "Remote Port");
            endpointsGrid.Columns.Add("LastState", "Last State");
            endpointsGrid.Columns.Add("FirstSeen", "First Seen");
            endpointsGrid.Columns.Add("LastSeen", "Last Seen");
            endpointsGrid.Columns.Add("SeenCount", "Seen Count");
            this.Controls.Add(endpointsGrid);

            refreshTimer = new Timer { Interval = 1000 };
            refreshTimer.Tick += (sender, e) => RefreshEndpointLearning();
            refreshTimer.Start();
        }

        public void Update(ISubject subject)
        {
            MessageCode code = (subject as Subject).Message.code;
            if (code == MessageCode.PROCESS_CHANGED)
            {
                learnedEndpoints.Clear();
                currentPid = 0;
                RenderTable();
                RefreshEndpointLearning();
            }
            else if (code == MessageCode.TURN_OFF)
            {
                lblStatus.Text = "Paused while application is OFF.";
            }
            else if (code == MessageCode.TURN_ON)
            {
                RefreshEndpointLearning();
            }
        }

        private void RefreshEndpointLearning()
        {
            Client client = ClientSingleton.GetClient();
            if (client == null || client.process == null)
            {
                lblStatus.Text = "No client selected.";
                return;
            }

            currentPid = client.process.Id;
            List<TcpEndpointInspector.TcpRow> rows = TcpEndpointInspector.GetRowsForPid(currentPid);
            DateTime now = DateTime.UtcNow;

            foreach (TcpEndpointInspector.TcpRow row in rows)
            {
                string key = row.RemoteIP + ":" + row.RemotePort;
                if (!learnedEndpoints.ContainsKey(key))
                {
                    learnedEndpoints[key] = new EndpointLearnRecord
                    {
                        RemoteIP = row.RemoteIP.ToString(),
                        RemotePort = row.RemotePort,
                        LastState = row.State.ToString(),
                        FirstSeenUtc = now,
                        LastSeenUtc = now,
                        SeenCount = 1
                    };
                }
                else
                {
                    EndpointLearnRecord record = learnedEndpoints[key];
                    record.LastState = row.State.ToString();
                    record.LastSeenUtc = now;
                    record.SeenCount++;
                }
            }

            lblStatus.Text = "Auto-learning TCP remote endpoints for PID " + currentPid +
                             " | Known endpoints: " + learnedEndpoints.Count;
            RenderTable();
        }

        private void RenderTable()
        {
            endpointsGrid.Rows.Clear();
            foreach (EndpointLearnRecord record in learnedEndpoints.Values.OrderByDescending(v => v.LastSeenUtc))
            {
                endpointsGrid.Rows.Add(
                    record.RemoteIP,
                    record.RemotePort.ToString(),
                    record.LastState,
                    record.FirstSeenUtc.ToLocalTime().ToString("HH:mm:ss"),
                    record.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss"),
                    record.SeenCount.ToString()
                );
            }
        }
    }
}
