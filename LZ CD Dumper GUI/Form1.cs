using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace LZ_CD_Dumper_GUI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }



        private void button2_Click(object sender, EventArgs e)
        {
            DriveInfo[] allDrives = DriveInfo.GetDrives();

            foreach (var drive in allDrives)
            {
                if (drive.DriveType == DriveType.CDRom)
                {
                    comboBox1.Items.Add(string.Format("{0}", drive.Name));
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            backgroundWorker1.RunWorkerAsync();
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            string drive_letter = string.Empty;

            Invoke((MethodInvoker)delegate
            {
                drive_letter = Regex.Match(comboBox1.Text, "[A-Z]{1}:").Value;
            });

            SafeFileHandle _hdev = CreateFile(string.Format(@"\\.\{0}", drive_letter), (uint)FileAccess.ReadWrite, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0, IntPtr.Zero);

            bool result = false;
            int bytesReturned = 0;

            IntPtr buffer = Marshal.AllocHGlobal(sizeof(ulong));

            result = DeviceIoControl(_hdev, CTL_CODE(0x00000007, 0x0017, 0, 1), IntPtr.Zero, 0, buffer, sizeof(ulong), ref bytesReturned, IntPtr.Zero);

            long disk_size = Marshal.ReadInt64(buffer);
            int speed = 0;

            Marshal.FreeHGlobal(buffer);

            Invoke((MethodInvoker)delegate
            {
                speed = int.Parse(comboBox2.Text);
                listBox1.Items.Clear();
                progressBar1.Value = 0;
                progressBar1.Maximum = (int)(disk_size / 2048);
            });

            int buffer_size = checkBox1.Checked ? 2352 + 296 : 2352;

            sptd_with_sense sptd = new sptd_with_sense();

            sptd.sptd.CdbLength = (byte)sptd.sptd.Cdb.Length;
            sptd.sptd.Length = (ushort)Marshal.SizeOf(sptd.sptd);
            sptd.sptd.DataIn = 1;// SCSI_IOCTL_DATA_IN;
            sptd.sptd.TimeOutValue = 10;
            sptd.sptd.DataBuffer = Marshal.AllocHGlobal(buffer_size);
            sptd.sptd.DataTransferLength = (uint)buffer_size;

            sptd.sptd.Cdb[0] = 0xbb;
            sptd.sptd.Cdb[2] = 0xff;
            sptd.sptd.Cdb[3] = 0xff;

            IntPtr sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
            Marshal.StructureToPtr(sptd, sptd_ptr, false);
            result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);
            Marshal.FreeHGlobal(sptd_ptr);

            sptd.sptd.Cdb[0] = 0xbe;
            sptd.sptd.Cdb[2] = 0x00;
            sptd.sptd.Cdb[3] = 0x00;

            List<string> c2errors = new List<string>();

            using (BinaryWriter bw = new BinaryWriter(new FileStream("dump.bin", FileMode.Create)))
            {
                for (int i = 0; i < disk_size / 2048; i++)
                {
                    sptd.sptd.Cdb[2] = (byte)(i >> 24);
                    sptd.sptd.Cdb[3] = (byte)(i >> 16);
                    sptd.sptd.Cdb[4] = (byte)(i >> 8);
                    sptd.sptd.Cdb[5] = (byte)(i >> 0);

                    byte[] raw_sample = new byte[buffer_size];

                    sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
                    Marshal.StructureToPtr(sptd, sptd_ptr, false);

                    result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);

                    Marshal.Copy(sptd.sptd.DataBuffer, raw_sample, 0, buffer_size);
                    Marshal.FreeHGlobal(sptd_ptr);
                    bw.Write(raw_sample, 0, 2352);

                    byte C2 = raw_sample[2352+294];

                    byte C3 = 0;
                    for (int c = 0; c < 294; c++)
                    {
                        C3 |= raw_sample[2352 + c];
                    }

                    bool error = false;

                    for (int err = 0; err < 294; err++)
                    {
                        if (raw_sample[2352 + err] != 0)
                        {
                            error = true;
                            continue;
                        }
                    }

                    //if(C2 != 0)
                    if(error)
                    {
                        c2errors.Add(i.ToString());
                        Invoke((MethodInvoker)delegate
                        {
                            listBox1.Items.Add(string.Format("LBA {0:D6}: C2 error detected", i));
                        });
                    }

                    Invoke((MethodInvoker)delegate
                    {
                        progressBar1.PerformStep();
                    });
                }
            }

            File.WriteAllLines("dump.txt", c2errors);
            _hdev.Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        class sptd_with_sense
        {
            public SCSI_PASS_THROUGH_DIRECT sptd = new SCSI_PASS_THROUGH_DIRECT();
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
            public byte[] sense = new byte[18];
        }

        [StructLayout(LayoutKind.Sequential)]
        class SCSI_PASS_THROUGH_DIRECT
        {
            public UInt16 Length;
            public byte ScsiStatus;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseInfoLength;
            public byte DataIn;
            public UInt32 DataTransferLength;
            public UInt32 TimeOutValue;
            public IntPtr DataBuffer;
            public UInt32 SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            //public byte[] Cdb = { 0xBE, 0, 0, 0, 0, 0, 0, 0, 1, 0b11111000, 0, 0, 0, 0, 0, 0 };
            public byte[] Cdb = { 0xBE, 0, 0, 0, 0, 0, 0, 0, 1, 0b11111100, 0, 0, 0, 0, 0, 0 };
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
             string fileName,
             uint fileAccess,
             uint fileShare,
             IntPtr securityAttributes,
             uint creationDisposition,
             uint flags,
             IntPtr template);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool DeviceIoControl(
            [In] SafeFileHandle hDevice,
            [In] uint dwIoControlCode,
            [In] IntPtr lpInBuffer,
            [In] int nInBufferSize,
            [Out] IntPtr lpOutBuffer,
            [Out] int nOutBufferSize,
            ref int lpBytesReturned,
            [In] IntPtr lpOverlapped);

        private static uint IOCTL_SCSI_PASS_THROUGH_DIRECT()
        {
            return CTL_CODE(IOCTL_SCSI_BASE, 0x0405, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);
        }

        static UInt32 IOCTL_SCSI_BASE = 0x00000004;
        static UInt32 METHOD_BUFFERED = 0;
        static UInt32 FILE_READ_ACCESS = 0x0001;
        static UInt32 FILE_WRITE_ACCESS = 0x0002;

        public static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
        {
            return (((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method));
        }

        private void button3_Click(object sender, EventArgs e)
        {
            backgroundWorker2.RunWorkerAsync();
        }

        private void backgroundWorker2_DoWork(object sender, DoWorkEventArgs e)
        {
            string drive_letter = string.Empty;

            Invoke((MethodInvoker)delegate
            {
                drive_letter = Regex.Match(comboBox1.Text, "[A-Z]{1}:").Value;
            });

            SafeFileHandle _hdev = CreateFile(string.Format(@"\\.\{0}", drive_letter), (uint)FileAccess.ReadWrite, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0, IntPtr.Zero);

            bool result = false;
            int bytesReturned = 0;

            int buffer_size = 2352 + 296;

            sptd_with_sense sptd = new sptd_with_sense();

            if (checkBox2.Checked)
            {
                sptd.sptd.Cdb[8] = 3;
                buffer_size *= 3;
            }

            sptd.sptd.CdbLength = (byte)sptd.sptd.Cdb.Length;
            sptd.sptd.Length = (ushort)Marshal.SizeOf(sptd.sptd);
            sptd.sptd.DataIn = 1;// SCSI_IOCTL_DATA_IN;
            sptd.sptd.TimeOutValue = 10;
            sptd.sptd.DataBuffer = Marshal.AllocHGlobal(buffer_size);
            sptd.sptd.DataTransferLength = (uint)buffer_size;

            UInt16 speed = 0;

            string _temp_str = string.Empty;
            Invoke((MethodInvoker)delegate
            {
                _temp_str = comboBox2.Text;
            });

            switch (_temp_str)
            {
                case "1":
                    speed = 0x1;
                    break;
                case "2":
                    speed = 0x2;
                    break;
                case "4":
                    speed = 0x4;
                    break;
                case "8":
                    speed = 0x8;
                    break;
                case "max":
                    speed = 0xffff;
                    break;
            }


            sptd.sptd.Cdb[0] = 0xbb;
            sptd.sptd.Cdb[2] = (byte)(speed >> 8);
            sptd.sptd.Cdb[3] = (byte)speed;

            IntPtr sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
            Marshal.StructureToPtr(sptd, sptd_ptr, false);
            result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);
            Marshal.FreeHGlobal(sptd_ptr);

            sptd.sptd.Cdb[0] = 0xbe;
            sptd.sptd.Cdb[2] = 0x00;
            sptd.sptd.Cdb[3] = 0x00;


            List<string> c2errors = new List<string>();

            int[] sectors = File.ReadAllLines("dump.txt").Select(x => int.Parse(x)).ToArray();

            Invoke((MethodInvoker)delegate
            {
                listBox1.Items.Clear();
                progressBar1.Value = 0;
                progressBar1.Maximum = sectors.Length;
            });

            using (BinaryWriter bw = new BinaryWriter(new FileStream("dump.bin", FileMode.Open)))
            {
                for (int i = 0; i < sectors.Length; i++)
                {
                    sptd.sptd.Cdb[2] = (byte)(sectors[i] >> 24);
                    sptd.sptd.Cdb[3] = (byte)(sectors[i] >> 16);
                    sptd.sptd.Cdb[4] = (byte)(sectors[i] >> 8);
                    sptd.sptd.Cdb[5] = (byte)(sectors[i] >> 0);

                    

                    byte[] raw_sample = new byte[buffer_size];

                    sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
                    Marshal.StructureToPtr(sptd, sptd_ptr, false);

                    result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);

                    Marshal.Copy(sptd.sptd.DataBuffer, raw_sample, 0, buffer_size);
                    Marshal.FreeHGlobal(sptd_ptr);
                    

                    byte C2 = raw_sample[2352 + 294];

                    byte C3 = 0;
                    for (int c = 0; c < 294; c++)
                    {
                        C3 |= raw_sample[2352 + c];
                    }

                    if (C2 != 0)
                    {
                        c2errors.Add(sectors[i].ToString());
                        Invoke((MethodInvoker)delegate
                        {
                            listBox1.Items.Add(string.Format("LBA {0:D6}: C2 error detected", sectors[i]));
                        });
                    }
                    else
                    {
                        bw.BaseStream.Seek(sectors[i] * 2352, SeekOrigin.Begin);
                        bw.Write(raw_sample, 0, 2352);
                    }

                    Invoke((MethodInvoker)delegate
                    {
                        progressBar1.PerformStep();
                    });
                }
            }

            File.WriteAllLines("dump.txt", c2errors);
            _hdev.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            backgroundWorker3.RunWorkerAsync();
        }

        private void backgroundWorker3_DoWork(object sender, DoWorkEventArgs e)
        {
            string drive_letter = string.Empty;

            Invoke((MethodInvoker)delegate
            {
                drive_letter = Regex.Match(comboBox1.Text, "[A-Z]{1}:").Value;
            });

            SafeFileHandle _hdev = CreateFile(string.Format(@"\\.\{0}", drive_letter), (uint)FileAccess.ReadWrite, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0, IntPtr.Zero);

            bool result = false;
            int bytesReturned = 0;

            byte[] _temp = new byte[2048];
            IntPtr toc_buffer_ptr = Marshal.AllocHGlobal(_temp.Length);
            result = DeviceIoControl(_hdev, IOCTL_CDROM_READ_TOC_EX(), toc_buffer_ptr, _temp.Length, toc_buffer_ptr, _temp.Length, ref bytesReturned, IntPtr.Zero);
            byte[] toc = new byte[bytesReturned];
            Marshal.Copy(toc_buffer_ptr, toc, 0, toc.Length);
            Marshal.FreeHGlobal(toc_buffer_ptr);

            File.WriteAllBytes("dump.toc", toc);
            
            _hdev.Close();

            int n_tracks = toc[3];

            List<string> cuesheet = new List<string>();
            cuesheet.Add("FILE \"dump.bin\" BINARY");

            for (int i = 0; i < n_tracks; i++)
            {
                int lba = 0;
                lba += toc[8 + 8 * i + 0] << 24;
                lba += toc[8 + 8 * i + 1] << 16;
                lba += toc[8 + 8 * i + 2] << 8;
                lba += toc[8 + 8 * i + 3] << 0;

                string mode = string.Empty;

                switch (toc[5 + 8 * i] & 0xf)
                {
                    case 0:
                        mode = "AUDIO";
                        break;
                    case 4:
                        mode = "MODE2/2352";
                        break;
                }

                cuesheet.Add(string.Format("  TRACK {0:D2} {1}", toc[6 + 8 * i], mode));
                cuesheet.Add(string.Format("    INDEX 01 {0}", lba2msf(lba)));
            }

            File.WriteAllLines("dump.cue", cuesheet);
        }

        private uint IOCTL_CDROM_READ_TOC_EX()
        {
            return 0x24054u;
        }

        private uint CDROM_TOC_FULL_TOC_DATA()
        {
            throw new NotImplementedException();
        }

        private object lba2msf(int lba)
        {
            int m = (lba / 75) / 60;
            int s = (lba / 75) % 60;
            int f = lba % 75;

            return string.Format("{0:D2}:{1:D2}:{2:D2}", m, s, f);
        }

        private uint IOCTL_CDROM_READ_TOC()
        {
            return CTL_CODE(2, 0, METHOD_BUFFERED, FILE_READ_ACCESS);
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            string drive_letter = string.Empty;

            Invoke((MethodInvoker)delegate
            {
                drive_letter = Regex.Match(comboBox1.Text, "[A-Z]{1}:").Value;
                dataGridView1.Rows.Clear();
            });

            SafeFileHandle _hdev = CreateFile(string.Format(@"\\.\{0}", drive_letter), (uint)FileAccess.ReadWrite, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0, IntPtr.Zero);

            bool result = false;
            int bytesReturned = 0;

            byte[] _temp = new byte[4096];
            IntPtr toc_buffer_ptr = Marshal.AllocHGlobal(_temp.Length);
            result = DeviceIoControl(_hdev, IOCTL_CDROM_READ_TOC_EX(), toc_buffer_ptr, _temp.Length, toc_buffer_ptr, _temp.Length, ref bytesReturned, IntPtr.Zero);
            byte[] toc = new byte[bytesReturned];
            Marshal.Copy(toc_buffer_ptr, toc, 0, toc.Length);
            Marshal.FreeHGlobal(toc_buffer_ptr);

            int n_tracks = toc[3];

            for (int i = 0; i < n_tracks; i++)
            {
                int lba = 0;
                lba += toc[8 + 8 * i + 0] << 24;
                lba += toc[8 + 8 * i + 1] << 16;
                lba += toc[8 + 8 * i + 2] << 8;
                lba += toc[8 + 8 * i + 3] << 0;

                string mode = string.Empty;

                int n = toc[6 + 8 * i];

                switch (toc[5 + 8 * i] & 0xf)
                {
                    case 0:
                        mode = "AUDIO";
                        break;
                    case 4:
                        mode = "DATA";
                        break;
                }

                if (mode == "DATA")
                {
                    int buffer_size = 2352 + 296;

                    sptd_with_sense sptd = new sptd_with_sense();

                    sptd.sptd.Cdb[2] = (byte)(lba >> 24);
                    sptd.sptd.Cdb[3] = (byte)(lba >> 16);
                    sptd.sptd.Cdb[4] = (byte)(lba >> 8);
                    sptd.sptd.Cdb[5] = (byte)(lba >> 0);

                    

                    sptd.sptd.CdbLength = (byte)sptd.sptd.Cdb.Length;
                    sptd.sptd.Length = (ushort)Marshal.SizeOf(sptd.sptd);
                    sptd.sptd.DataIn = 1;// SCSI_IOCTL_DATA_IN;
                    sptd.sptd.TimeOutValue = 10;
                    sptd.sptd.DataBuffer = Marshal.AllocHGlobal(buffer_size);
                    sptd.sptd.DataTransferLength = (uint)buffer_size;

                    byte[] raw_sample = new byte[2352 + 296];

                    IntPtr sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
                    Marshal.StructureToPtr(sptd, sptd_ptr, false);

                    result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);

                    Marshal.Copy(sptd.sptd.DataBuffer, raw_sample, 0, buffer_size);
                    Marshal.FreeHGlobal(sptd_ptr);


                    mode = string.Format("MODE{0}/2352", raw_sample[15]);
                }

                Invoke((MethodInvoker)delegate
                {
                    dataGridView1.Rows.Add(n, lba, mode);
                });
            }

            _hdev.Close();
        }
    }
}
