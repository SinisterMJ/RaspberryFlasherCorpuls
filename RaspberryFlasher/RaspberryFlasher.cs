using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using log4net;
using log4net.Config;

namespace RaspberryFlasher
{
    public partial class FlashingApplication : Form
    {
        private Thread worker;
        private static readonly ILog log = LogManager.GetLogger("CorpSim.FlashingApplication");
        private SD_Card_Handler sdHandler;
        public FlashingApplication()
        {
            XmlConfigurator.Configure();
            log.Info("Starting application.");
            
            InitializeComponent();
            sdHandler = new SD_Card_Handler(this);
            string path_cli = ConfigurationManager.AppSettings["CLI.Tool"];
            if (path_cli[path_cli.Length - 1] != '\\')
                ConfigurationManager.AppSettings["CLI.Tool"] += "\\";

            string path_images = ConfigurationManager.AppSettings["ImageFolder"];
            if (path_images[path_images.Length - 1] != '\\')
                ConfigurationManager.AppSettings["ImageFolder"] += "\\";

            log.Info("Path for CLI Tools: " + ConfigurationManager.AppSettings["CLI.Tool"]);
            log.Info("Path for Images: " + ConfigurationManager.AppSettings["ImageFolder"]);

            NameValueCollection config = ConfigurationManager.GetSection("productKeys") as NameValueCollection;
            
            foreach (var color in config.AllKeys)
            {
                string basePath = ConfigurationManager.AppSettings["ImageFolder"];
                string version = ConfigurationManager.AppSettings["Image_Version"];
                string fileName = basePath + "Corpuls_Simulation_" + config[color] + "_" + version + ".img";
                if (!System.IO.File.Exists(fileName))
                {
                    log.Fatal("Image file not found: " + fileName);
                    MessageBox.Show("Critical error. Image file not found. Please check log for more information.");
                    Application.Exit();
                }
            }

            if (!System.IO.File.Exists(ConfigurationManager.AppSettings["CLI.Tool"] + "CommandLineFlasher.exe"))
            {
                log.Fatal("DiskImmager not found.");
                MessageBox.Show("Critical error. SD Card Flash Utility not found. Please contact developer.");
                Application.Exit();
            }

            if (!System.IO.File.Exists(ConfigurationManager.AppSettings["CLI.Tool"] + "devcon.exe"))
            {
                log.Fatal("Devcon not found.");
                MessageBox.Show("Critical error. Devcon not found. Please contact developer.");
                Application.Exit();
            }
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

        void SetTextBoxVisible(bool visible)
        {
            this.UIThreadAsync(delegate
            {
                textBox_SerialNumber.Visible = visible;
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

        public void SetLabelText(string text, int id)
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

        public void SetLabelProductCodeText(string text)
        {
            this.UIThreadAsync(delegate
            {
                labelProductCode.Text = text;
                Refresh();
            });            
        }        

        public void SetLabelVisibility(bool enabled, int id)
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

        void FlashImage(string imgPath, string letterPath, int id)
        {
            SetLabelText("SD Karte " + id.ToString() + ": Schreiben initialisiert.", id);
            int count = 0;
            const int max_tries = 6;
            int errorCode = -1;
            while (count < max_tries)
            {
                Process process = new Process();
                process.StartInfo.FileName = ConfigurationManager.AppSettings["CLI.Tool"] + "CommandLineFlasher.exe";
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
                    errorCode = process.ExitCode;
                    string text = "SD Karte " + id.ToString() + ": Fehlerhaft. Fehlercode " + process.ExitCode.ToString();
                    log.Error("ExitCode[" + id + "]:" + process.ExitCode);
                    if (count < max_tries)
                    {
                        text += ". Neustart in 10s. Versuch " + count.ToString() + "/6";
                    }
                    SetLabelText(text, id);
                    
                    count++;
                    if (count != max_tries)
                        Thread.Sleep(10000);
                }
                else
                {
                    log.Debug("ExitCode[" + id + "]:" + process.ExitCode);
                    return;
                }
            }
            SetLabelText("SD Karte konnte nicht bespielt werden. Fehlercode: " + errorCode, id);
            log.Fatal("Could not write to SD card " + id);
        }

        void RunCommand(string colorCode)
        {
            SetLabelText("", 1);

            for (int i = 2; i <= 8; ++i)
            {
                SetLabelVisibility(false, i);
                SetLabelText("", i);
            }

            List<string> drives = sdHandler.GetAllDrives();
            log.Info("Total SD cards found: " + drives.Count.ToString());

            foreach (var drive in drives)
                log.Info("Found SD Card at " + drive);

            SetLabelText("Gesamt: " + drives.Count + " Karten gefunden", 1);

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
                //FlashImage(fileName, drive, count_thread++);
                Thread local = new Thread(() => FlashImage(fileName, drive, count_thread++));
                local.Start();
                allThreads.Add(local);
            }

            foreach (Thread t in allThreads)
                t.Join();

            ResetForm(false);
        }

        void ResetForm(bool complete = true)
        {
            for (int i = 2; i <= 8 && complete; ++i)
            {
                SetLabelVisibility(false, i);
                SetLabelText("", i);
            }

            SetTextBoxEnabled(true);
            SetButtonExitEnabled(true);
            SetTextBoxVisible(true);
            SetTextBoxText("");
            SetTextBoxFocus();
            SetLabelProductCodeText("Produktcode scannen");            
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

            SetTextBoxVisible(false);
            SetButtonExitEnabled(false);
            SetLabelProductCodeText("Schreibe Corpuls Simulation " + color);
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
