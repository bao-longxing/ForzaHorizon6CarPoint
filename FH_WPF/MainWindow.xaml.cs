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

            //初始化日志
            ClsLogger.Init(txtLog, txtScriptLog, txtPointLog);

            //初始化OBS
            if (!ClsObs.IsConnected)
            {
                ClsObs.ConnectAsync("192.168.31.110", 4455, "").GetAwaiter().GetResult();
                AppendLog("[信息] OBS 已初始化");
            }

            //初始化OCR
            ClsOCR.Initialize();
            AppendLog("[信息] OCR 已初始化");

            //初始化ROI
            ClsROI.LoadTargetRectsFromJson("targetRects.json");

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
            ClsGameControl.UpCarPoint(txtCarFactory.Text, txtCarType.Text, false);
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
            try
            {
                Thread.Sleep(3000);
                ClsLogicContorl_Ghub.Move(-4096, -4096, false);
                ClsLogicContorl_Ghub.Move(1490, 371, true);
                ClsLogicContorl_Ghub.ClickMouse(1);


                //ClsGameControl.UpCarPoint(txtCarFactory.Text, txtCarType.Text, true);
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 调用 GHUB 接口失败: " + ex.Message);
            }
        }

        private void btnROI_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] ROI 按钮点击");
        }

        private void btnMousePos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ClsMousePos.IsRunning)
                {
                    ClsMousePos.Stop();
                }
                else
                {
                    ClsMousePos.Start();
                }
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
                    try
                    {
                        txtLog.CaretIndex = txtLog.Text.Length;
                        txtLog.ScrollToEnd();
                    }
                    catch { }
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
                    try
                    {
                        txtPointLog.CaretIndex = txtPointLog.Text.Length;
                        txtPointLog.ScrollToEnd();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void AppendScriptLog(string message)
        {
            try
            {
                if (txtScriptLog != null)
                {
                    if (string.IsNullOrEmpty(txtScriptLog.Text))
                        txtScriptLog.Text = message;
                    else
                        txtScriptLog.Text += Environment.NewLine + message;
                    try
                    {
                        txtScriptLog.CaretIndex = txtScriptLog.Text.Length;
                        txtScriptLog.ScrollToEnd();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
