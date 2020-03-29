using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TA400
{
    public partial class Form1 : Form
    {
        byte[] last_datapacket = new byte[2000];
        int last_datapacketpos = 0;
        short labelval = 0;
        short labeltempval = 0;
        string labelbin = "";

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            timer1.Enabled = !timer1.Enabled;
        }

        private void send()
        {

            if (serialPort1.IsOpen == false)
                serialPort1.Open();

            
            byte[] bytes = { 0xAA, 0xBB, 0x1 };
            serialPort1.Write(bytes, 0, 3);

        }

        private void serialPort1_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            serialPort1.ReadTimeout = 1000;
            string line;
            int bytesread;
            byte[] bytes = new byte[2000];
            bytesread = 0;

            try
            {
                //#AA #BB #1E #03 #33 #A6 #2F #B9 #B4 #96 #99 #BF #E2 #B2 #33 #BF #00 #00 #00 #80 #09 #01 #00 #00 #20 #41 #00 #00 #20 #41 #00 #00 #00 #18 #09 #00 #00 #20 #00 #00 #00 #00 #02 #00 #FE #09

                bytesread = serialPort1.Read(bytes, 0, serialPort1.BytesToRead);
                //Console.WriteLine(bytesread.ToString());

                // find start
                for (int bstart = 0;bstart< bytesread; bstart++)
                { 
                    if (bytes[bstart] == 0xAA && bytes[bstart+1] == 0xBB)
                    {
                        if (last_datapacket[0] != 0)
                        { // existing datapacket

                            short val = 0;
                            // valid packet
                            // split packets at 2
                            // temp = 265 flow = 3459596
                            // 0 - 1 = 48042 AA BB 2 - 3 = 265 09 01   4 - 5 = 12812 0C 32 6 - 7 = 48059 BB BB 8 - 9 = 1689 99 06  10 - 11 = 49089 C1 BF   12 - 13 = 55061 15 D7   14 - 15 = 48993 61 BF   16 - 17 = 0 00 00   18 - 19 = 32768 00 80   20 - 21 = 266 0A 01 22 - 23 = 0 00 00   24 - 25 = 16672 20 41   26 - 27 = 0 00 00   28 - 29 = 16672 20 41   30 - 31 = 0 00 00   32 - 33 = 1024 00 04    34 - 35 = 9 09 00   36 - 37 = 40960 00 A0   38 - 39 = 0 00 00   40 - 41 = 0 00 00   42 - 43 = 4 04 00   44 - 45 = 2380 4C 09


                            short tempc = bytes2Short(last_datapacket, 21, 20);

                            int i = Convert.ToInt16(numericUpDown1.Value);
                            if (numericUpDown2.Value>0)
                                last_datapacket[i+1] = (byte)(last_datapacket[i+1] >> Convert.ToInt32(numericUpDown2.Value));
                            
                            short flow = bytes2Short(last_datapacket, i+1, i);

                            labelbin = Convert.ToString(last_datapacket[i+i], 2).PadLeft(8, '0');
                            labelval = flow;
                            labeltempval = tempc;

                            //int flow = last_datapacket[4] * 256 + last_datapacket[5];

                            //Console.Write("temp C =" + tempc.ToString() + " flow=" + flow.ToString()); //+ " b7 = " + b7.ToString());

                            string ashex = "";
                            for (int b = 0; b < 46; b = b + 2)
                            {
                                string bh = last_datapacket[b].ToString("X2");
                                string b2h = last_datapacket[b + 1].ToString("X2");

                                //string b = last_datapacket[b + 1].ToString("X2");
                                //string b2 = last_datapacket[b + 1].ToString("X2");
                                string a = "0x" + b2h + bh;
                                val = Convert.ToInt16(a, 16); // int.Parse(b2h + bh, System.Globalization.NumberStyles.HexNumber);
                                //val = last_datapacket[b + 1] * 256 + last_datapacket[b];
                                //Console.Write(b.ToString() + "-" + (b + 1).ToString() + "=" + val.ToString() + " ");
                                //Console.Write(val.ToString() + " ");
                                ashex = ashex + " " + bh + " " + b2h;
                            }

                            // next message starts here
                            //Console.WriteLine();
                            Console.WriteLine(ashex);

                            last_datapacket = new byte[2000];
                            last_datapacket[0] = 0;
                            last_datapacketpos = 0;
                            serialPort1.DiscardInBuffer();
                        }
                        else
                        { //  no existing datapacket
                            for (int b = bstart; b < bytesread; b++)
                            {
                                last_datapacket[last_datapacketpos] = bytes[b];
                                last_datapacketpos++;
                            }
                            break;
                        }

                    } 
                    else
                    {
                        last_datapacket[last_datapacketpos] = bytes[bstart];
                        last_datapacketpos++;
                        
                    }
                }

            }
            catch (Exception ex)
            {

            }

        }

        short bytes2Short(byte[] bytes, int msb, int lsb)
        {

            string bh = bytes[lsb].ToString("X2");
            string b2h = bytes[msb].ToString("X2");
            short val = 0;
            //string b = last_datapacket[b + 1].ToString("X2");
            //string b2 = last_datapacket[b + 1].ToString("X2");
            string a = "0x" + b2h + bh;
            val = Convert.ToInt16(a, 16);
            return val;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            timer1.Enabled = false;

            if (serialPort1.IsOpen == true)
                serialPort1.Close();

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            send();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            last_datapacket[0] = 0;
            serialPort1.ReceivedBytesThreshold = 46; // 1 datapackets 
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            label1.Text = labelval.ToString();
            label2.Text = labeltempval.ToString();
            label3.Text = labelbin;
            textBox1.Text = labelbin + "\r\n" + textBox1.Text;
        }
    }
}
