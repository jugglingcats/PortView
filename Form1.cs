using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Management;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PortView
{
    public partial class PortViewForm : Form
    {
        private const int WM_DEVICECHANGE = 0x219;

        private int GWL_EXSTYLE = -20;

        private bool updating;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20 | 0x80000 | 0x80; // 0x80000 /* WS_EX_LAYERED */ /*| 0x20 WS_EX_TRANSPARENT */ /*| 0x80 WS_EX_TOOLWINDOW */;
                return cp;
            }
        }

        public PortViewForm()
        {
            InitializeComponent();

            ShowInTaskbar = false;

            listView1.SmallImageList = imageList1;
            listView1.LargeImageList = imageList1;
            //            listView1.StateImageList = imageList1;
            listView1.Padding = new System.Windows.Forms.Padding(15, 0, 0, 0);

            BackColor = Color.LimeGreen;
            TransparencyKey = Color.LimeGreen;

            StartPosition = FormStartPosition.CenterScreen;
            Size = new System.Drawing.Size(250, 150);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;  // no borders

            TopMost = true;
            Visible = true;

            UpdatePorts();
        }

        private string desc(uint pid, ManagementBaseObject p)
        {
            switch (pid)
            {
                case 0x5740:
                    return "Espruino";
                case 0x5742:
                    return "Glowboard";
                case 0x374b:
                    return "Nucleo";
                default:
                    return p["Description"].ToString();
            }
        }

        private ListEntry itemise(ManagementBaseObject p)
        {
            var result = new ListEntry
            {
                Port = p["DeviceID"].ToString(),
                Caption = p.Properties["Caption"].ToString()
            };

            Regex r = new Regex("USB\\\\VID_([0-9A-F]+)&PID_([0-9A-F]+)");
            Match m = r.Match(p["PNPDeviceId"].ToString());
            if (m.Success)
            {
                var vid = Convert.ToUInt32(m.Groups[1].ToString(), 16);
                var pid = Convert.ToUInt32(m.Groups[2].ToString(), 16);
                result.VID = vid;
                result.PID = pid;
                if (vid == 0x0483)
                {
                    result.Caption = $"{desc(pid, p)} ({p["DeviceID"].ToString()})";
                }
            }
            return result;
        }

        private IEnumerable<ListEntry> EnumPorts()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM WIN32_SerialPort"))
            {
                string[] portnames = SerialPort.GetPortNames();

                var ports = searcher.Get().Cast<ManagementBaseObject>().ToList();
                return (from n in portnames
                        join p in ports on n equals p["DeviceID"].ToString()
                        select itemise(p)).ToList();
            }
        }

        private void Flicker(ListViewItem item, int image)
        {
            Task.Run(() =>
            {
                for (int n = 0; n < 10; n++)
                {
                    Thread.Sleep(200);
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        item.ImageIndex = n % 2 == 0 ? image : 10;
                    });
                }
            });
        }

        private void UpdatePorts(bool first = false)
        {
            if (this.updating)
            {
                return;
            }
            this.updating = true;
            Task.Run(() =>
            {
                Thread.Sleep(200); // delay

                this.BeginInvoke((MethodInvoker)delegate
                {
                    var items = EnumPorts();
                    var existing = listView1.Items.Cast<ListViewItem>().Select(i => (ListEntry)i.Tag);

                    foreach (var item in existing.Where(p => !p.Connected && items.Any(p2 => p2.Equals(p))))
                    {
                        item.Connected = true;
                        var listViewItem = listView1.Items.Find(item.Port, false);
                        Debug.WriteLine($"{1} was re-connected", item.Caption);

                        if (listViewItem.Length > 0)
                        {
                            Flicker(listViewItem[0], 1);
                        }
                    }
                    foreach (var item in items.Where(p => !existing.Any(p2 => p2.Equals(p))))
                    {
                        item.Connected = true;
                        Debug.WriteLine($"{1} was connected", item.Caption);
                        var listViewItem = listView1.Items.Add(item.Port, item.Caption, 1);
                        listViewItem.Tag = item;
                    }
                    foreach (var item in existing.Where(p => !items.Any(p2 => p2.Equals(p))))
                    {
                        item.Connected = false;
                        var listViewItem = listView1.Items.Find(item.Port, false);
                        Debug.WriteLine($"{1} was disconnected", item.Caption);

                        if ( listViewItem.Length > 0 )
                        {
                            Flicker(listViewItem[0], 0);
                        }
                    }
                    this.updating = false;
                });
            });
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            switch (m.Msg)
            {
                case WM_DEVICECHANGE:
                    Task.Delay(500).ContinueWith((task) => { UpdatePorts(); });
                    break;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetTransparency();
        }

        private void SetTransparency()
        {
            User32_SetLayeredWindowAttributes(this.Handle, (TransparencyKey.B << 16) + (TransparencyKey.G << 8) + TransparencyKey.R, 160, 0x01 | 0x02);
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            int style = GetWindowLong(this.Handle, GWL_EXSTYLE);
            if ((style & 0x80000) == 0x80000)
            {
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
                SetWindowLong(this.Handle, GWL_EXSTYLE, style & ~(0x20 | 0x80000 | 0x80));
                floatingToolStripMenuItem.Checked = false;
                alwaysOnTopToolStripMenuItem.Enabled = true;
                TopMost = alwaysOnTopToolStripMenuItem.Checked;
            }
            else
            {
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                SetWindowLong(this.Handle, GWL_EXSTYLE, style | (0x20 | 0x80000 | 0x80));
                SetTransparency();
                floatingToolStripMenuItem.Checked = true;
                alwaysOnTopToolStripMenuItem.Enabled = false;
                TopMost = true;
            }
            Update();
        }

        private void alwaysOnTopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            alwaysOnTopToolStripMenuItem.Checked = !alwaysOnTopToolStripMenuItem.Checked;
            TopMost = alwaysOnTopToolStripMenuItem.Checked;
            Update();
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.Items.Clear();
            UpdatePorts();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes")]
        private static extern bool User32_SetLayeredWindowAttributes(IntPtr hWnd, int crKey, byte bAlpha, int attributes);

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
    }

    class ListEntry
    {
        public uint VID;
        public uint PID;
        public string Port;
        public string Caption;

        public bool Connected { get; internal set; }

        public override bool Equals(object obj)
        {
            ListEntry other = obj as ListEntry;

            return other.VID == this.VID && other.PID == this.PID;
        }

        public override int GetHashCode()
        {
            return (int)(VID + PID);
        }
    }
}
