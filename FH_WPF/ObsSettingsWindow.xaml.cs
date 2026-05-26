using System;
using System.Windows;

namespace FH_WPF
{
    public partial class ObsSettingsWindow : Window
    {
        public string? ObsIp { get; private set; }
        public int ObsPort { get; private set; }
        public string? ObsPassword { get; private set; }

        public ObsSettingsWindow(string? currentIp, int currentPort, string? currentPassword)
        {
            InitializeComponent();
            txtIp.Text = currentIp ?? string.Empty;
            txtPort.Text = currentPort > 0 ? currentPort.ToString() : string.Empty;
            txtPassword.Password = currentPassword ?? string.Empty;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            var ip = txtIp.Text?.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show(this, "请输入 OBS IP", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtPort.Text, out var port) || port <= 0)
            {
                MessageBox.Show(this, "请输入有效端口", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ObsIp = ip;
            ObsPort = port;
            ObsPassword = txtPassword.Password;
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}