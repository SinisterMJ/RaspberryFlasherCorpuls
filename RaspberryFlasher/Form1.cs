using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RaspberryFlasher
{
    public partial class Form1 : Form
    {
        private Thread worker;
        List<string> excludedDrives;
        public Form1()
        {
            InitializeComponent();
            excludedDrives = new List<string>();
            string listAll = ConfigurationManager.AppSettings["Exclude_Drives"];
            foreach (string s in listAll.Split(' '))
                excludedDrives.Add(s);            
        }

        void SetTextBoxText(string newText)
        {
            this.UIThreadAsync(delegate
            {
                textBox_SerialNumber.Text = newText;
                Refresh();
            });
        }

        void SetTextBoxEnabled(bool enabled)
        {
            this.UIThreadAsync(delegate
            {
                textBox_SerialNumber.Enabled = enabled;
                Refresh();
            });
        }

        void SetTextBoxFocus()
        {
            this.UIThreadAsync(delegate
            {
                textBox_SerialNumber.Focus();
            });
        }

        void SetLabelText(string text, int id)
        {
            string labelName = "label" + id.ToString();

            foreach(Control c in this.Controls)
            {
                if (c.Name == labelName)
                {
                    this.UIThreadAsync(delegate
                    {
                        c.Text = text;
                        Refresh();
                    });
                }
            }
        }

        void SetLabelVisibility(bool enabled, int id)
        {
            string labelName = "label" + id.ToString();

            foreach (Control c in Controls)
            {
                if (c.Name == labelName)
                {
                    this.UIThreadAsync(delegate
                    {
                        c.Visible = enabled;
                        Refresh();
                    });
                }
            }
        }

        void SetButtonExitEnabled(bool enabled)
        {
            this.UIThreadAsync(delegate
            {
                buttonExit.Enabled = enabled;
            });
        }

        private bool GetColorCode(out string colorCode)
        {
            string productCode = textBox_SerialNumber.Text;

            NameValueCollection config = ConfigurationManager.GetSection("productKeys") as NameValueCollection;
            colorCode = "";
            
            if (config.AllKeys.Contains(productCode))
            {
                colorCode = config[productCode];
            }

            return colorCode != "";
        }

        List<string> DriveLetters()
        {
            List<string> result = new List<string>();
            Process process = new Process();
            process.StartInfo.FileName = "wmic";
            process.StartInfo.Arguments = "logicaldisk where drivetype=2 get Caption,Size";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();

            Regex rx = new Regex(@".?(.):.*?.*?", RegexOptions.Compiled);
            MatchCollection matches = rx.Matches(output);

            foreach (Match match in matches)
            {
                GroupCollection groups = match.Groups;
                if (excludedDrives.Contains(groups[1].Value))
                    continue;

                result.Add(groups[1].Value);
            }

            return result;
        }

        void FlashImage(string imgPath, string letterPath, int id)
        {
            Process process = new Process();
            process.StartInfo.FileName = ConfigurationManager.AppSettings["CLI.Tool"];
            process.StartInfo.Arguments = imgPath + " " + letterPath;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;

            //process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);

            process.OutputDataReceived += 
                (object _sender, DataReceivedEventArgs _args) =>
                    OutputHandler(id, _sender, _args);

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
            SetLabelText("SD Karte " + id.ToString() + ": Beendet.", id);
            if (process.ExitCode != 0)
            {
                SetLabelText("SD Karte " + id.ToString() + ": Fehlerhaft. Fehlercode " + process.ExitCode.ToString(), id); ;
            }
        }

        void RunCommand(string colorCode)
        {
            for (int i = 1; i <= 6; ++i)
            {
                SetLabelVisibility(false, i);
                SetLabelText("", i);
            }

            //* Create your Process
            List<string> drives = DriveLetters();

            if (drives.Count != 6)
            {
                string text = (drives.Count < 6 ? "Weniger" : "Mehr") + " als 6 Datenträger gefunden. Fortfahren?";
                if (MessageBox.Show(text, "Datenträger Anzahl", MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    ResetForm();
                    return;
                }
            }

            string basePath = ConfigurationManager.AppSettings["ImageFolder"];
            string version = ConfigurationManager.AppSettings["Image_Version"];
            string fileName = basePath + "Corpuls_Simulation_" + colorCode + "_" + version + ".img";
            int count = 1;
            int count_thread = 1;
            List<Thread> allThreads = new List<Thread>();

            foreach (string drive in drives)
            {
                SetLabelVisibility(true, count++);
                Thread local = new Thread(() => FlashImage(fileName, drive, count_thread++));
                local.Start();
                allThreads.Add(local);
            }
                        
            while(true)
            {
                bool stop = true;
                foreach(Thread t in allThreads)
                {
                    stop = stop && (!t.IsAlive);
                }

                if (stop)
                    break;
            }

            ResetForm();
        }

        void ResetForm()
        {
            SetTextBoxEnabled(true);
            SetButtonExitEnabled(true);
            SetTextBoxText("");
            SetTextBoxFocus();
        }
        void OutputHandler(int id, object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (outLine.Data != null)
            {
                string[] bytes = outLine.Data.Split('/');
                Int64 percent = Convert.ToInt64(bytes[0]) * 100 / Convert.ToInt64(bytes[1]);
                SetLabelText("SD Karte " + id.ToString() + ": " + percent.ToString("D" + 2) + "% geschrieben.", id);
            }
        }

        private void workingThread()
        {
            string color;
            if (!GetColorCode(out color))
            {
                MessageBox.Show("Ungültiger Produktcode. Bitte erneut scannen.");
                ResetForm();
                return;
            }

            SetTextBoxEnabled(false);
            SetButtonExitEnabled(false);
            RunCommand(color);
        }

        private void textBox_SerialNumber_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char) Keys.Enter)
            {
                e.Handled = true;
                ThreadStart work = workingThread;
                worker = new Thread(work);
                worker.Start();
            }
        }

        private void buttonExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
