using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FH_WPF
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly DateTime _startTime;
        public MainWindow()
        {
            InitializeComponent();
            _startTime = DateTime.Now;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                UpdateCurrentTime();
                UpdateRunTime();
            }
            catch { }
        }

        // 更新当前时间到 StatusBarItem txtTime
        public void UpdateCurrentTime()
        {
            try
            {
                if (txtTime != null)
                {
                    txtTime.Content = $"当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
            }
            catch { }
        }

        // 更新运行时长到 StatusBarItem txtRunTime
        public void UpdateRunTime()
        {
            try
            {
                if (txtRunTime != null)
                {
                    var elapsed = DateTime.Now - _startTime;
                    var hours = (int)elapsed.TotalHours;
                    var runStr = string.Format("运行时长：{0:00}:{1:00}:{2:00}", hours, elapsed.Minutes, elapsed.Seconds);
                    txtRunTime.Content = runStr;
                }
            }
            catch { }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            }
        }

        private void btnDelectCar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtCarFactory != null) txtCarFactory.Text = string.Empty;
                if (txtCarType != null) txtCarType.Text = string.Empty;
                AppendLog("[信息] 已从车库移除车辆");
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 移除车辆失败: " + ex.Message);
            }
        }

        private void btnPoint_Click(object sender, RoutedEventArgs e)
        {
            AppendPointLog($"[{DateTime.Now:HH:mm:ss}] 开始消耗点数...");
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] OBS 按钮点击");
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] KeyBoard 按钮点击");
        }

        private void btnOCRTest_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] OCR 测试 按钮点击");
        }

        private void btnTestGameControl_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] 脚本测试 按钮点击");
            Thread.Sleep(3000);
            try
            {
                if (!ClsLogicContorl_Ghub.IsInitialized)
                {
                    AppendLog("[警告] GHUB 设备未初始化，尝试打开...");
                    if (!ClsLogicContorl_Ghub.DeviceOpen())
                    {
                        AppendLog("[错误] 打开 GHUB 设备失败: " + ClsLogicContorl_Ghub.LastError);
                        return;
                    }
                    AppendLog("[信息] GHUB 设备初始化成功");
                }

                ClsLogicContorl_Ghub.Move(-4000, -4000);
                ClsLogicContorl_Ghub.MouseDown(3);
                ClsLogicContorl_Ghub.MouseUp(3);
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 调用 GHUB 接口失败: " + ex.Message);
            }
            ClsLogicContorl_Ghub.ClickMouse(3);
        }

        private void btnROI_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] ROI 按钮点击");
        }

        private void btnMousePos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pos = Mouse.GetPosition(this);
                AppendLog($"[信息] 鼠标位置: ({pos.X:0.##}, {pos.Y:0.##})");
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 获取鼠标位置失败: " + ex.Message);
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AppendLog(string message)
        {
            try
            {
                if (txtLog != null)
                {
                    var prefix = DateTime.Now.ToString("HH:mm:ss");
                    if (string.IsNullOrEmpty(txtLog.Text))
                        txtLog.Text = $"[{prefix}] {message}";
                    else
                        txtLog.Text += Environment.NewLine + $"[{prefix}] {message}";
                }
            }
            catch { }
        }

        private void AppendPointLog(string message)
        {
            try
            {
                if (txtPointLog != null)
                {
                    if (string.IsNullOrEmpty(txtPointLog.Text))
                        txtPointLog.Text = message;
                    else
                        txtPointLog.Text += Environment.NewLine + message;
                }
            }
            catch { }
        }
    }
}
