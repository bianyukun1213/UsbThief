using System.Windows.Forms;

namespace UsbThief
{
    public partial class LogForm : Form
    {
        public LogForm()
        {
            InitializeComponent();
            FormClosing += LogForm_FormClosing;
        }
        private void LogForm_Load(object sender, System.EventArgs e)
        {
            if (Form1.logger == null)
                Form1.logger = NLog.LogManager.GetCurrentClassLogger();
        }
        private void LogForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Hide();
            e.Cancel = true;
        }
    }
}

