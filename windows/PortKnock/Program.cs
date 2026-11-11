using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortKnock
{
    class Knock
    {
        public string Kind;
        public int Port;
        public override string ToString() { return Kind == "ICMP" ? "ICMP" : Kind + " " + Port; }
    }

    class MainForm : Form
    {
        TextBox host = new TextBox(), test = new TextBox(), delay = new TextBox(), port = new TextBox();
        ComboBox kind = new ComboBox();
        ListBox list = new ListBox();
        Button knockBtn = new Button();
        Label status = new Label();
        List<Knock> knocks = new List<Knock>();
        static readonly string SettingsPath =
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PortKnock.ini");

        public MainForm()
        {
            Text = "Port Knock";
            ClientSize = new Size(330, 300);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            AddLabel("Host / IP", 10, 14);
            host.SetBounds(90, 10, 230, 24);
            AddLabel("Test port", 10, 46);
            test.SetBounds(90, 42, 70, 24);
            AddLabel("Delay (ms)", 170, 46);
            delay.SetBounds(250, 42, 70, 24);
            delay.Text = "300";

            list.SetBounds(10, 76, 310, 100);

            kind.DropDownStyle = ComboBoxStyle.DropDownList;
            kind.Items.AddRange(new object[] { "TCP", "UDP", "ICMP" });
            kind.SelectedIndex = 0;
            kind.SetBounds(10, 186, 70, 24);
            port.SetBounds(90, 186, 80, 24);

            var add = new Button { Text = "+" };
            add.SetBounds(180, 185, 65, 26);
            add.Click += (s, e) => AddKnock();
            var del = new Button { Text = "-" };
            del.SetBounds(255, 185, 65, 26);
            del.Click += (s, e) => RemoveKnock();

            knockBtn.Text = "Knock";
            knockBtn.SetBounds(10, 224, 310, 32);
            knockBtn.Click += async (s, e) => await RunAsync();

            status.Text = "Closed";
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = Color.White;
            status.BackColor = Color.FromArgb(192, 57, 43);
            status.SetBounds(10, 262, 310, 28);

            Controls.AddRange(new Control[] { host, test, delay, list, kind, port, add, del, knockBtn, status });

            LoadSettings();
            FormClosing += (s, e) => SaveSettings();
        }

        void SaveSettings()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("host=" + host.Text.Trim());
                lines.Add("test=" + test.Text.Trim());
                lines.Add("delay=" + delay.Text.Trim());
                foreach (var k in knocks) lines.Add("knock=" + k.Kind + " " + k.Port);
                File.WriteAllLines(SettingsPath, lines.ToArray(), new System.Text.UTF8Encoding(false));
            }
            catch { } // e.g. folder not writable: just skip saving
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "host") host.Text = val;
                    else if (key == "test") test.Text = val;
                    else if (key == "delay") { if (val.Length > 0) delay.Text = val; }
                    else if (key == "knock")
                    {
                        var parts = val.Split(' ');
                        int p = 0;
                        if (parts[0] != "TCP" && parts[0] != "UDP" && parts[0] != "ICMP") continue;
                        if (parts[0] != "ICMP" && (parts.Length < 2 || !int.TryParse(parts[1], out p))) continue;
                        var kn = new Knock { Kind = parts[0], Port = p };
                        knocks.Add(kn);
                        list.Items.Add(kn.ToString());
                    }
                }
            }
            catch { } // corrupt file: start with defaults
        }

        void AddLabel(string t, int x, int y)
        {
            Controls.Add(new Label { Text = t, Left = x, Top = y, AutoSize = true });
        }

        void AddKnock()
        {
            string k = (string)kind.SelectedItem;
            int p = 0;
            if (k != "ICMP" && (!int.TryParse(port.Text, out p) || p < 1 || p > 65535))
            {
                MessageBox.Show("Invalid port");
                return;
            }
            var kn = new Knock { Kind = k, Port = p };
            knocks.Add(kn);
            list.Items.Add(kn.ToString());
            SaveSettings();
        }

        void RemoveKnock()
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            list.Items.RemoveAt(i);
            knocks.RemoveAt(i);
            SaveSettings();
        }

        void SetStatus(string text, Color c)
        {
            status.Text = text;
            status.BackColor = c;
        }

        async Task RunAsync()
        {
            string h = host.Text.Trim();
            int d, t = 0;
            if (h.Length == 0 || knocks.Count == 0)
            {
                MessageBox.Show("Enter host and at least one knock");
                return;
            }
            if (!int.TryParse(delay.Text, out d) || d < 0 ||
                (test.Text.Trim().Length > 0 && !int.TryParse(test.Text, out t)))
            {
                MessageBox.Show("Invalid number");
                return;
            }

            SaveSettings();
            knockBtn.Enabled = false;
            SetStatus("Knocking...", Color.Gray);
            try
            {
                foreach (var k in knocks.ToArray())
                {
                    await SendKnock(h, k);
                    await Task.Delay(d);
                }
                await Task.Delay(500);
                bool ok = await CheckOpen(h, t);
                SetStatus(ok ? "Open" : "Closed",
                          ok ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 57, 43));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, Color.FromArgb(192, 57, 43));
            }
            knockBtn.Enabled = true;
        }

        static async Task SendKnock(string host, Knock k)
        {
            try
            {
                if (k.Kind == "ICMP")
                {
                    using (var p = new Ping()) await p.SendPingAsync(host, 300);
                }
                else if (k.Kind == "UDP")
                {
                    using (var u = new UdpClient()) await u.SendAsync(new byte[] { 0 }, 1, host, k.Port);
                }
                else
                {
                    // TCP: only the first SYN matters; a connection error is expected
                    using (var c = new TcpClient())
                    {
                        await Task.WhenAny(c.ConnectAsync(host, k.Port), Task.Delay(300));
                    }
                }
            }
            catch { }
        }

        static async Task<bool> CheckOpen(string host, int testPort)
        {
            try
            {
                if (testPort > 0)
                {
                    using (var c = new TcpClient())
                    {
                        var conn = c.ConnectAsync(host, testPort);
                        if (await Task.WhenAny(conn, Task.Delay(3000)) != conn) return false;
                        await conn;
                        return c.Connected;
                    }
                }
                using (var p = new Ping())
                {
                    var r = await p.SendPingAsync(host, 1500);
                    return r.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
