using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace RaspberryFlasher
{
    public partial class Form1 : Form
    {
        private Thread worker;
        public Form1()
        {
            InitializeComponent();
        }

        void setTextBoxText(string newText)
        {
            this.UIThreadAsync(delegate
            {
                textBox_SerialNumber.Text = newText;
            });
        }

        void setTextBoxEnabled(bool enabled)
        {
            this.UIThreadAsync(delegate
            {
                this.textBox_SerialNumber.Enabled = enabled;
            });
        }

        private bool getColorCode(out string colorCode)
        {
            colorCode = "";
            string productCode = textBox_SerialNumber.Text;

            if (productCode == "97048.01")
                colorCode = "BLACK";

            if (productCode == "97048.02")
                colorCode = "RED";

            if (productCode == "97048.03")
                colorCode = "GREEN";

            if (productCode == "97048.04")
                colorCode = "BLUE";

            if (productCode == "97048.05")
                colorCode = "ORANGE";

            if (productCode == "97048.06")
                colorCode = "PINK";

            if (productCode == "97048.07")
                colorCode = "PURPLE";

            if (productCode == "97048.08")
                colorCode = "CAMO";

            return colorCode != "";
        }

        void runCommand(string colorCode)
        {
            //* Create your Process
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c DIR";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            //* Set your output and error (asynchronous) handlers
            process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            //* Start process and handlers
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            setTextBoxEnabled(true);
        }

        void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            //* Do your stuff with the output (write to console/log/StringBuilder)
            Console.WriteLine(outLine.Data);
        }

        private void workingThread()
        {
            string color;
            if (!getColorCode(out color))
            {
                setTextBoxText("");
                MessageBox.Show("Ungültiger Produktcode. Bitte erneut scannen.");
                return;
            }

            setTextBoxEnabled(false);
            runCommand(color);
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
    }
}
