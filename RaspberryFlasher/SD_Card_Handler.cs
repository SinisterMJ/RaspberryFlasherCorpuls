using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RaspberryFlasher
{
    class SD_Card_Handler
    {
        private string vendorID;
        private string sdcardID;
        private readonly FlashingApplication mainForm;
        private static readonly ILog log = LogManager.GetLogger("CorpSim.FlashingApplication");
        public SD_Card_Handler(FlashingApplication form)
        {
            mainForm = form;
            vendorID = ConfigurationManager.AppSettings["Hardware_ID"];
            sdcardID = ConfigurationManager.AppSettings["Configured_Capture_Name"];
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

        public List<string> GetAllDrives()
        {            
            BringAllOnline();
            List<string> drives = DriveLetters();

            return drives;
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
            mainForm.SetLabelVisibility(true, 1);

            mainForm.SetLabelText("Checke aktuellen Stand der USB Laufwerke", 1);
            var stats = ReadDriveStats();

            mainForm.SetLabelText("Überprüfe auf einzigartige IDs", 1);
            if (DistributeUniqueIDs(stats))
            {
                log.Info("IDs were changed. Restarting USB device");
                int waitTime = Convert.ToInt32(ConfigurationManager.AppSettings["WaitTime"]);
                mainForm.SetLabelText("IDs geändert. USB Treiber wird neu gestartet. Dies dauert etwa " + (waitTime / 1000).ToString() + " Sekunden.", 1);
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
    }
}
