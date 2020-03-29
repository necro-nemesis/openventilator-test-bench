using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;

using NAudio.Wave;
using NAudio.CoreAudioApi;

// audio part from: https://github.com/swharden/Csharp-Data-Visualization/tree/master/projects/18-09-19_microphone_FFT_revisited

namespace open_ventilator_tester
{
    public partial class Form1 : Form
    {
        // MICROPHONE ANALYSIS SETTINGS
        private int RATE = 44100; // sample rate of the sound card
        private int BUFFERSIZE = (int)Math.Pow(2, 11); // must be a multiple of 2

        // prepare class objects
        public BufferedWaveProvider bwp;

        // log headers
        bool bHeaderWritten = false;
        string csvHeader = "";

        private static Mutex mutLog = new Mutex();
        private static Mutex mut = new Mutex();

        // TTI CPX400 DP 2 x 420 W power supply, connected to USB (or LAN or GPIB but here we use USB for quick & simple solution)
        System.IO.Ports.SerialPort PSU = new System.IO.Ports.SerialPort();

        BindingList<Cyclepoint> cyclepoints = new BindingList<Cyclepoint>();
        bool bTestRunning = false;
        int currentStepNumber = -1;
        Cyclepoint currentStep = new Cyclepoint();
        string logfilename = "";

        DataTable dtLogData = new DataTable();
        //DateTime dt0 = DateTime.MinValue;  // any date will do, just know which you use!
        double last_motorcurrent = 0;
        double last_motorvoltage = 0;
        double last_motortemp = 0;
        double last_flow = 0;
        double last_pressure = 0;
        double last_rpm = 0.0;
        double last_rpm_setpoint = 0.0;

        List<List<DataPoint>> datapoints = new List<List<DataPoint>>(); // = { new List<DataPoint>(), new List<DataPoint>(), new List<DataPoint>(), new List<DataPoint>(), new List<DataPoint>() };
        List<DataField> datanames = new List<DataField>();

        const int DATA_TIME = 0;
        const int DATA_MOTOR_RPM = 1;
        const int DATA_MOTOR_RPM_SETPOINT = 2;
        const int DATA_FLOW = 3;
        const int DATA_MOTORVOLTAGE = 4;
        const int DATA_MOTORCURRENT = 5;

        const int DATA_MAX = 6;

        public Form1()
        {
            InitializeComponent();

            SetupGraphLabels();
            StartListeningToMicrophone();
            timerAudioReplot.Enabled = true;
        }

        #region AUDIOHANDLING

        void AudioDataAvailable(object sender, WaveInEventArgs e)
        {
            bwp.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        public void SetupGraphLabels()
        {
            scottPlotUC1.plt.Title("Microphone PCM Data");
            scottPlotUC1.plt.YLabel("Amplitude (PCM)");
            scottPlotUC1.plt.XLabel("Time (ms)");
            scottPlotUC1.Render();

            scottPlotUC2.plt.Title("Microphone FFT Data");
            scottPlotUC2.plt.YLabel("Power (raw)");
            scottPlotUC2.plt.XLabel("Frequency (kHz)");
            scottPlotUC2.Render();
        }

        public void StartListeningToMicrophone(int audioDeviceNumber = 0)
        {
            WaveIn wi = new WaveIn();
            wi.DeviceNumber = audioDeviceNumber;
            wi.WaveFormat = new NAudio.Wave.WaveFormat(RATE, 1);
            wi.BufferMilliseconds = (int)((double)BUFFERSIZE / (double)RATE * 1000.0);
            wi.DataAvailable += new EventHandler<WaveInEventArgs>(AudioDataAvailable);
            bwp = new BufferedWaveProvider(wi.WaveFormat);
            bwp.BufferLength = BUFFERSIZE * 2;
            bwp.DiscardOnBufferOverflow = true;
            try
            {
                wi.StartRecording();
            }
            catch
            {
                string msg = "Could not record from audio device!\n\n";
                msg += "Is your microphone plugged in?\n";
                msg += "Is it set as your default recording device?";
                MessageBox.Show(msg, "ERROR");
            }
        }

        private void timerAudioReplot_Tick(object sender, EventArgs e)
        {
            // turn off the timer, take as long as we need to plot, then turn the timer back on
            timerAudioReplot.Enabled = false;
            PlotLatestAudioData();
            timerAudioReplot.Enabled = true;

        }


        public int numberOfDraws = 0;
        public bool needsAutoScaling = true;
        public void PlotLatestAudioData()
        {
            // check the incoming microphone audio
            int frameSize = BUFFERSIZE;
            var audioBytes = new byte[frameSize];
            bwp.Read(audioBytes, 0, frameSize);

            // return if there's nothing new to plot
            if (audioBytes.Length == 0)
                return;
            if (audioBytes[frameSize - 2] == 0)
                return;

            // incoming data is 16-bit (2 bytes per audio point)
            int BYTES_PER_POINT = 2;

            // create a (32-bit) int array ready to fill with the 16-bit data
            int graphPointCount = audioBytes.Length / BYTES_PER_POINT;

            // create double arrays to hold the data we will graph
            double[] pcm = new double[graphPointCount];
            double[] fft = new double[graphPointCount];
            double[] fftReal = new double[graphPointCount / 2];

            // populate Xs and Ys with double data
            for (int i = 0; i < graphPointCount; i++)
            {
                // read the int16 from the two bytes
                Int16 val = BitConverter.ToInt16(audioBytes, i * 2);

                // store the value in Ys as a percent (+/- 100% = 200%)
                pcm[i] = (double)(val) / Math.Pow(2, 16) * 200.0;
            }

            int maxIndex = 0;
            double maxLevelAtFreq = 0;
            // calculate the full FFT
            fft = FFT(pcm, out maxIndex);

            // determine horizontal axis units for graphs
            double pcmPointSpacingMs = RATE / 1000;
            double fftMaxFreq = RATE / 2;
            double fftPointSpacingHz = fftMaxFreq / graphPointCount;

            // calculate frequency where peak occurs
            maxLevelAtFreq = fftPointSpacingHz * maxIndex * 2;

            // just keep the real half (the other half imaginary)
            Array.Copy(fft, fftReal, fftReal.Length);

            // use a color array for displaying data from low to high density
            Color[] colors = new Color[]
            {
    ColorTranslator.FromHtml("#440154"),
    ColorTranslator.FromHtml("#39568C"),
    ColorTranslator.FromHtml("#1F968B"),
    ColorTranslator.FromHtml("#73D055"),
            };

            // plot the Xs and Ys for both graphs
            scottPlotUC1.plt.Clear();
            scottPlotUC1.plt.PlotSignal(pcm, pcmPointSpacingMs,0,0,Color.Blue);
            scottPlotUC2.plt.Clear();
            scottPlotUC2.plt.PlotSignal(fftReal, fftPointSpacingHz,0,0, colorByDensity: colors);

            // optionally adjust the scale to automatically fit the data
            if (needsAutoScaling)
            {
                
                scottPlotUC1.plt.AxisAuto();
                scottPlotUC2.plt.AxisAuto();
                
                needsAutoScaling = false;
            }

            //scottPlotUC1.PlotSignal(Ys, RATE);

            numberOfDraws += 1;
            lblMaxFreq.Text = "FFT at " + maxLevelAtFreq.ToString("F0") + " Hz";

            //lblStatus.Text = $"Analyzed and graphed PCM and FFT data {numberOfDraws} times";
            scottPlotUC1.Render();
            scottPlotUC2.Render();
            // this reduces flicker and helps keep the program responsive
            Application.DoEvents();

        }

        private void autoScaleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            needsAutoScaling = true;
        }

        private void infoMessageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string msg = "";
            msg += "left-click-drag to pan\n";
            msg += "right-click-drag to zoom\n";
            msg += "middle-click to auto-axis\n";
            msg += "double-click for graphing stats\n";
            MessageBox.Show(msg);
        }

        private void websiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/swharden/Csharp-Data-Visualization");
        }

        public double[] FFT(double[] data, out int maxIndex)
        {
            double maxValueSoFar = 0;
            maxIndex = 0;

            double[] fft = new double[data.Length];
            System.Numerics.Complex[] fftComplex = new System.Numerics.Complex[data.Length];
            for (int i = 0; i < data.Length; i++)
                fftComplex[i] = new System.Numerics.Complex(data[i], 0.0);
            Accord.Math.FourierTransform.FFT(fftComplex, Accord.Math.FourierTransform.Direction.Forward);
            for (int i = 0; i < data.Length; i++)
            { 
                fft[i] = fftComplex[i].Magnitude;
                if (fft[i] > maxValueSoFar)
                {
                    maxValueSoFar = fft[i];
                    maxIndex = i;
                }
            }
            return fft;

        }


        #endregion

        static private double addPointsToGraph(System.Windows.Forms.DataVisualization.Charting.Series series, List<DataPoint> points)
        {
            double avg = 0;
            avg = 0;
            if (points.Count == 0)
                return 0;
            
            foreach (DataPoint dp in points)
            {
                DateTime datepoint = new DateTime(dp.x);
                
                series.Points.AddXY(datepoint.ToOADate(), dp.y);
                //series.Points.AddXY(dp.x, dp.y);
                avg = avg + dp.y;
            }
            avg = avg / points.Count;
            return avg;
        }



        private void Form1_Load(object sender, EventArgs e)
        {



            PSU.PortName = "COM10";
            PSU.BaudRate = 9600;

            try
            {
                PSU.DataReceived += (OnPSU_Rx);
                PSU.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Problems opening PSU port:" + ex.Message);
            }

            Uni_T_Devices.UT372 rpm_meter = new Uni_T_Devices.UT372();

            rpm_meter.openUSB();

            //rpm_meter.dumpUSBData();

            //MessageBox.Show(rpm_meter.parseSerialInputToRPM("070?<3=7<60655>607;007885").ToString());
            //Console.WriteLine(rpm_meter.parseSerialInputToRPM("07;7;7;7;7;655>607;007885").ToString());
            //Console.WriteLine(rpm_meter.parseSerialInputToRPM("0607;7;7;7;655>607;007885").ToString());

            dataGridView1.DataSource = cyclepoints;
            dataGridView1.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

            dataGridView2.Rows.Clear();

            datanames.Add(new DataField("Time", 0));
            datanames.Add(new DataField("MRPM", 1));
            datanames.Add(new DataField("MRPM Set", 2));
            datanames.Add(new DataField("Flow", 3));
            datanames.Add(new DataField("MVoltage",4));
            datanames.Add(new DataField("MCurrent", 5));

            chart1.Series.Clear();
            csvHeader = "";

            foreach (DataField df in datanames)
            {
                csvHeader = csvHeader + df.Name + ";";
                dtLogData.Columns.Add(df.Name);
                datapoints.Add(new List<DataPoint>());

                chart1.Series.Add(df.Name);
                chart1.Series.Last().ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
                chart1.Series.Last().XValueType = System.Windows.Forms.DataVisualization.Charting.ChartValueType.Time;

                if (df.Index == DATA_MOTORCURRENT)
                {
                    chart1.Series.Last().YAxisType = System.Windows.Forms.DataVisualization.Charting.AxisType.Secondary;
                } else {
                    chart1.Series.Last().YAxisType = System.Windows.Forms.DataVisualization.Charting.AxisType.Primary;
                }
            }
            //dt0 = DateTime.Now;  // any date will do, just know which you use!
            chart1.ChartAreas[0].AxisX.LabelStyle.Format = "HH:mm:ss";

        }

        private void btnOpenTestcycle_Click(object sender, EventArgs e)
        {
            try
            {
                XMLHandler x = new XMLHandler();

                OpenFileDialog o = new OpenFileDialog();
                o.ShowDialog();
                if (o.FileName != "")
                { 
                    cyclepoints = x.DeSerializeCyclepointsFromXML(o.FileName);
                    dataGridView1.DataSource = cyclepoints;
                }

            }
            catch (Exception ex)
            {

            }
        }

        private void btnSaveParameters_Click(object sender, EventArgs e)
        {
            try
            {
                XMLHandler x = new XMLHandler();

                SaveFileDialog o = new SaveFileDialog();
                o.ShowDialog();
                if (o.FileName!="")
                    x.SerializeCyclepoints2XML(cyclepoints, o.FileName);

            }
            catch (Exception ex)
            {

            }
        }

        void UpdateGraphAndUI()
        {

            mut.WaitOne();

            string timenow = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss.fff");

            if (datapoints[DATA_MOTOR_RPM_SETPOINT].Count > 0)
            {
                last_rpm_setpoint = addPointsToGraph(chart1.Series[DATA_MOTOR_RPM_SETPOINT], datapoints[DATA_MOTOR_RPM_SETPOINT]);
            }

            if (datapoints[DATA_MOTORCURRENT].Count > 0)
            {
                last_motorcurrent = addPointsToGraph(chart1.Series[DATA_MOTORCURRENT], datapoints[DATA_MOTORCURRENT]);
            }

            if (datapoints[DATA_MOTORVOLTAGE].Count > 0)
            {
                last_motorvoltage = addPointsToGraph(chart1.Series[DATA_MOTORVOLTAGE], datapoints[DATA_MOTORVOLTAGE]);
            }

            mutLog.WaitOne();

            DataRow r = dtLogData.NewRow();
            r["Time"] = timenow;

            for (int i = 1;i<DATA_MAX;i++)
            {
                if (datapoints[i].Count>0)
                    r[datanames[i].Index] = datapoints[i].Average(selector => selector.y);
                
                datapoints[i] = new List<DataPoint>();

            }

            dtLogData.Rows.Add(r);
            mutLog.ReleaseMutex();

            mut.ReleaseMutex();
        }

        private void tmrMain_Tick(object sender, EventArgs e)
        {
            lblTime.Text = DateTime.Now.ToShortTimeString();

            if (bTestRunning)
            {
                lblStatus.Text = "RUNNING";
            }
            else
            {
                lblStatus.Text = "STOPPED";
            }
            
            if (bTestRunning)
            {
                queryAndLogPSU(); // query and log power supply TTI CPX400PD - connected via USB serial port or LAN TCP

                currentStep.dura = currentStep.dura - tmrMain.Interval/1000.0f;
                
                lblStepRemainingSec.Text = currentStep.dura.ToString()  + " s remaining";
                if (toolStripProgressBar1.Maximum >= currentStep.dura)
                    toolStripProgressBar1.Value = toolStripProgressBar1.Maximum - Convert.ToInt16(currentStep.dura);

                dataGridView1.CurrentCell = dataGridView1.Rows[currentStepNumber].Cells[0];

                mut.WaitOne();

                // add datapoint to log and graph
                datapoints[DATA_MOTOR_RPM_SETPOINT].Add(new DataPoint(currentStep.outp));
                
                mut.ReleaseMutex();

                if (currentStep.dura <= 0)
                {
                    if (cyclepoints.Count > currentStepNumber+1)
                    {
                        changeStep(currentStepNumber + 1);
                    }
                    else
                    {
                        bTestRunning = false;
                        changeStep(0, false);
                        stopMotor();
                    }

                }
                UpdateGraphAndUI();

            }
        }

        private void queryAndLogPSU()
        {

            if (PSU.IsOpen)
            {
                // this queries voltage and current from power supply
                // V1O ?; I1O ? 
                PSU.WriteLine("V1O?;I1O?");
            }
        }
        private void btnStart_Click(object sender, EventArgs e)
        {

            if (cyclepoints.Count==0)
            {
                MessageBox.Show("Empty cycle, check cycle!");
                return;
            }
            SaveFileDialog sfdial = new SaveFileDialog();
            sfdial.Filter = "Comma Separated Value (*.CSV)|*.CSV";
            sfdial.ShowDialog();
            logfilename = sfdial.FileName;
            if (logfilename == "")
            {
                MessageBox.Show("Select a log - file.", "Check filename?", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            for (int i = 0; i < chart1.Series.Count; i++)
            {
                chart1.Series[i].Points.Clear();
            }

            toolStripProgressBar1.Minimum = 0;
            toolStripProgressBar1.Maximum = cyclepoints.Count - 1;

            changeStep(0);
            bTestRunning = true;
            tmrWriteLog.Enabled = true;
            bHeaderWritten = false;

        }

        private void changeStep(int step, bool controlMotor = true)
        {
            currentStepNumber = step;
            
            dataGridView1.CurrentCell = dataGridView1.Rows[currentStepNumber].Cells[0];
            dataGridView1.Rows[currentStepNumber].Selected = true;
            currentStep = new Cyclepoint();

            currentStep.dura = cyclepoints[currentStepNumber].dura;
            currentStep.outp = cyclepoints[currentStepNumber].outp;

            toolStripProgressBar1.Value = 0;
            toolStripProgressBar1.Maximum = Convert.ToInt16(cyclepoints[currentStepNumber].dura);
            
            if (controlMotor)
            {
                sendToMotor(currentStep.outp);
            }
        }

        void sendToMotor(int o)
        {
            serialMotor.Open();
            serialMotor.Write(o.ToString());
            serialMotor.Close();
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            stopMotor();

            tmrWriteLog.Enabled = false;
        }

        private void stopMotor()
        {
            serialMotor.Open();
            serialMotor.Write("0");
            serialMotor.Close();

            bTestRunning = false;

        }


        private void writeLog()
        {
            try
            {

                string csvContent = "";

                mutLog.WaitOne();
                foreach (DataRow r in dtLogData.Rows)
                {
                    for (int i = 0; i < dtLogData.Columns.Count; i++)
                    {
                        csvContent = csvContent + r[i].ToString() + ";";
                    }
                    csvContent = csvContent + "\r\n";
                }

                System.IO.StreamWriter file = new System.IO.StreamWriter(logfilename, true);

                //    // Write the DataPoints into the file.

                if (bHeaderWritten == false)
                {
                    file.WriteLine(csvHeader);
                    bHeaderWritten = true;
                }

                file.Write(csvContent);

                file.Close();

                dtLogData.Rows.Clear();

                mutLog.ReleaseMutex();

                long length = new System.IO.FileInfo(logfilename).Length / 1000;
                lblLogStatus.Text = DateTime.Now.ToString() + " log " + length.ToString() + " kt";
            }
            catch (Exception ex)
            {
                lblLogStatus.Text = DateTime.Now.ToString() + " LOG ERROR: " + ex.Message;
            }


        }

        private void tmrWriteLog_Tick(object sender, EventArgs e)
        {
            writeLog();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (PSU.IsOpen)
                PSU.Close();

        }

        private void OnPSU_Rx(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            string a = PSU.ReadLine();
            Console.WriteLine(a);
            Single v = 0.0f;

            try
            {
                v = Convert.ToSingle(a.Replace("A", "").Replace("V","").Replace(".",",").Replace("\r",""));
            }
            catch (Exception ex)
            {

            }

            mut.WaitOne();

            if (a.Contains("V"))
            {
                // add datapoint to log and graph
                datapoints[DATA_MOTORVOLTAGE].Add(new DataPoint(v));

            }
            if (a.Contains("A"))
            {
                // add datapoint to log and graph
                datapoints[DATA_MOTORCURRENT].Add(new DataPoint(v));

            }
            mut.ReleaseMutex();

            // 12.649V
            // 0.023A
        }

        private void serialMotor_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {

        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            if (bTestRunning)
                return;

            DialogResult a = MessageBox.Show("Are you sure you want to calibrate ESC ? This sends 0, pause, 180 pause, 0.", "Run ESC calibration?", MessageBoxButtons.YesNoCancel);

            if (a == DialogResult.Yes)
            {
                sendToMotor(0);
                Thread.Sleep(2000);
                sendToMotor(180);
                Thread.Sleep(2000);
                sendToMotor(0);

            }
        }

        private void btnMotorManual_Click(object sender, EventArgs e)
        {
            try
            {
                sendToMotor(Convert.ToInt16(txtMotorManual.Text));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:" + ex.Message);
            }
        }
    }
}
