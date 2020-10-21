﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Management;
using log4net;
using log4net.Config;

namespace RaspberryFlasher
{
    public partial class FlashingApplication : Form
    {
        private Thread worker;
        private static readonly ILog log = LogManager.GetLogger("CorpSim.FlashingApplication");
        public FlashingApplication()
        {
            XmlConfigurator.Configure();
            log.Info("Starting application.");
            
            InitializeComponent();
            string path_cli = ConfigurationManager.AppSettings["CLI.Tool"];
            if (path_cli[path_cli.Length - 1] != '\\')
                ConfigurationManager.AppSettings["CLI.Tool"] += "\\";

            string path_images = ConfigurationManager.AppSettings["ImageFolder"];
            if (path_images[path_images.Length - 1] != '\\')
                ConfigurationManager.AppSettings["ImageFolder"] += "\\";

            log.Info("Path for CLI Tools: " + ConfigurationManager.AppSettings["CLI.Tool"]);
            log.Info("Path for Images: " + ConfigurationManager.AppSettings["ImageFolder"]);
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

        struct DriveStats
        {
            public int id { get; }
            public bool online { get; }
            public string unique_id { get; }

            public DriveStats(int id, bool online, string unique_id)
            {
                this.id = id;
                this.online = online;
                this.unique_id = unique_id;
            }
        }

        List<DriveStats> ReadDriveStats()
        {
            List<DriveStats> result = new List<DriveStats>();
            Process process = new Process();
            process.StartInfo.FileName = "diskpart.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            process.StandardInput.WriteLine("list disk");
            process.StandardInput.WriteLine("exit");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string[] outputs = output.Split('\n');

            foreach (string line in outputs)
            {
                if (line.Contains("Online") || line.Contains("Offline"))
                {
                    var regex = new Regex(".*([0-9]).* (.*line)");
                    var matches = regex.Matches(line);
                    int id = -1; 
                    bool online = false;
                    foreach (Match match in matches)
                    {
                        var coll = match.Groups;
                        id = Convert.ToInt32(coll[1].Value);
                        online = (coll[2].Value == "Online");
                    }

                    DriveStats entry = new DriveStats(id, online, "");
                    result.Add(entry);
                }
            }

            for (int i = 0; i < result.Count; ++i)
            {
                process.Start();
                process.StandardInput.WriteLine("select disk " + result[i].id);
                process.StandardInput.WriteLine("uniqueid disk");
                process.StandardInput.WriteLine("exit");
                string output_id = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (string line in output_id.Split('\n'))
                {
                    if (line.Contains("ger-ID"))
                    {
                        var regex = new Regex("ger-ID: ([0-9a-fA-F]*)");
                        var matches = regex.Matches(line);
                        string unique_id = "";
                        foreach (Match match in matches)
                        {
                            var coll = match.Groups;
                            unique_id = coll[1].Value;
                        }

                        if (unique_id == "")
                        {
                            result.RemoveAt(i);
                            i--;
                            continue;
                        }
                        result[i] = new DriveStats(result[i].id, result[i].online, unique_id);
                        log.Debug("Found drive. ID: " + result[i].id + " Online: " + result[i].online + " UID: " + unique_id);
                    }
                }
            }

            return result;
        }

        void SetUniqueID(int id, string new_unique)
        {
            Process process = new Process();
            process.StartInfo.FileName = "diskpart.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            process.StandardInput.WriteLine("select disk " + id.ToString());
            process.StandardInput.WriteLine("uniqueid disk id=" + new_unique);
            process.StandardInput.WriteLine("exit");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            log.Debug("Changed UID on disk " + id + " to " + new_unique + ".");
        }

        bool DistributeUniqueIDs(List<DriveStats> stats)
        {
            bool changed = false;
            List<string> all_ids = new List<string>();
            List<string> multiple_ids = new List<string>();

            foreach (var drive in stats)
            {
                if (all_ids.Contains(drive.unique_id))
                {
                    multiple_ids.Add(drive.unique_id);
                }

                all_ids.Add(drive.unique_id);
                all_ids = all_ids.Distinct().ToList();
                multiple_ids = multiple_ids.Distinct().ToList();
            }

            foreach (var id in multiple_ids)
                log.Debug("Multiple ID found: " + id);

            foreach (var drive in stats)
            {
                if (!drive.online)
                {
                    if (multiple_ids.Contains(drive.unique_id))
                    {
                        int intValue = int.Parse(drive.unique_id, System.Globalization.NumberStyles.HexNumber);
                        int startValue = intValue - 1;
                        while (intValue != startValue)
                        {                            
                            string hexValue = (++intValue).ToString("X8");
                            if (!all_ids.Contains(hexValue))
                            {
                                SetUniqueID(drive.id, hexValue);
                                all_ids.Add(hexValue);
                                changed = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Error occured. Drive is offline, but also has a unique id. Unknown error. Please contact developer.");
                        log.Fatal("Error occured. Drive is offline, but also has a unique id. Unknown error. Please contact developer.");
                        Application.Exit();
                    }
                }
            }

            return changed;
        }

        void RestartUSBReader()
        {
            Process process = new Process();
            process.StartInfo.FileName = ConfigurationManager.AppSettings["CLI.Tool"] + "devcon.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;

            log.Info("Shutting down USB reader hardware device.");
            process.StartInfo.Arguments = "disable \"@" + ConfigurationManager.AppSettings["Hardware_ID"] + "\"\\*\"";
            process.Start();
            process.WaitForExit();

            log.Info("Starting USB reader hardware device.");
            process.StartInfo.Arguments = "enable \"@" + ConfigurationManager.AppSettings["Hardware_ID"] + "\"\\*\"";
            process.Start();
            process.WaitForExit();
        }

        bool BringAllOnline()
        {
            SetLabelVisibility(true, 1);
            
            SetLabelText("Checke aktuellen Stand der USB Laufwerke", 1);
            var stats = ReadDriveStats();

            SetLabelText("Überprüfe auf einzigartige IDs", 1);
            if (DistributeUniqueIDs(stats))
            {
                log.Info("IDs were changed. Restarting USB device");
                int waitTime = Convert.ToInt32(ConfigurationManager.AppSettings["WaitTime"]);
                SetLabelText("IDs geändert. USB Treiber wird neu gestartet. Dies dauert etwa " + (waitTime / 1000).ToString() + " Sekunden.", 1);
                RestartUSBReader();
                Thread.Sleep(waitTime);
            }
            
            return true;
        }

        List<string> DriveLetters()
        {
            List<string> result = new List<string>();
            var usbDrives = GetUsbDevices();
            var drives = usbDrives.ToList();

            foreach (var drive in drives)
            {
                if (drive.Substring(3) != "0")
                    continue;

                result.Add(drive.Substring(0, 1));
            }
            
            return result;
        }

        IEnumerable<string> GetUsbDevices()
        {
            string sdName = ConfigurationManager.AppSettings["Configured_Capture_Name"];
            IEnumerable<string> usbDrivesLetters = from drive in new ManagementObjectSearcher("select * from Win32_DiskDrive WHERE InterfaceType='USB' AND Caption='" + sdName + "' AND MediaType='Removable Media'").Get().Cast<ManagementObject>()
                                                   from o in drive.GetRelated("Win32_DiskPartition").Cast<ManagementObject>()
                                                   from i in o.GetRelated("Win32_LogicalDisk").Cast<ManagementObject>()
                                                   select string.Format("{0} {1}", i["Name"], o["Index"]);

            return usbDrivesLetters;
        }

        void FlashImage(string imgPath, string letterPath, int id)
        {
            Process process = new Process();
            process.StartInfo.FileName = ConfigurationManager.AppSettings["CLI.Tool"] + "CommandLineDiskImager.exe";
            process.StartInfo.Arguments = "\"" + imgPath + "\" " + letterPath;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;

            process.OutputDataReceived += 
                (object _sender, DataReceivedEventArgs _args) =>
                    OutputHandler(id, _sender, _args);

            log.Debug("Starting process[" + id + "]: " + process.StartInfo.FileName);
            log.Debug("Arguments[" + id + "]: " + process.StartInfo.Arguments);

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
            SetLabelText("SD Karte " + id.ToString() + ": Beendet.", id);
            if (process.ExitCode != 0)
            {
                SetLabelText("SD Karte " + id.ToString() + ": Fehlerhaft. Fehlercode " + process.ExitCode.ToString(), id); ;
            }
            log.Debug("ExitCode[" + id + "]:" + process.ExitCode);
        }

        void RunCommand(string colorCode)
        {
            for (int i = 1; i <= 6; ++i)
            {
                SetLabelVisibility(false, i);
                SetLabelText("", i);
            }

            if (!BringAllOnline())
            {
                log.Error("Nicht alle SD Karten wurden erfolgreich gestartet. Bitte ARO kontaktieren.");
            }
            List<string> drives = DriveLetters();
            log.Info("Total SD cards found: " + drives.Count.ToString());

            foreach (var drive in drives)
                log.Info("Found SD Card at " + drive);

            int count_warning = Convert.ToInt32(ConfigurationManager.AppSettings["Num_SD_Cards"]);
            
            if (drives.Count != count_warning && ConfigurationManager.AppSettings["Show_Warning"] != "False")
            {
                string text = (drives.Count < count_warning ? "Weniger" : "Mehr") + " als " + count_warning.ToString() + " Datenträger gefunden. Fortfahren?";
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

            log.Debug("Spawning threads to write images.");

            foreach (string drive in drives)
            {
                SetLabelVisibility(true, count++);
                Thread local = new Thread(() => FlashImage(fileName, drive, count_thread++));
                local.Start();
                allThreads.Add(local);
            }

            foreach (Thread t in allThreads)
                t.Join();

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

            log.Info("Writing " + color + " images to SD cards.");

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
