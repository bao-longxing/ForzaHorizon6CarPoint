using OpenCvSharp;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;

namespace FH_WPF
{
    public partial class MainWindow : System.Windows.Window
    {
        #region Fields
        private readonly DispatcherTimer _timer;
        private readonly DateTime _startTime;
        // 运行/计时相关
        private DateTime? _raceStartTime;
        private DateTime? _carPointStartTime;
        private bool _raceRunning = false;
        private bool _carPointRunning = false;
        private DateTime? _lastPointUpdateTime;
        private TimeSpan? _pointUpdateInterval;
        private int _currentPoint = 0;
        private const int MaxPoint = 999;
        private const int PointModeSingleLoopPoint = 30;
        private ClsKeyboardHook? _keyboardHook;
        private bool UpCarIsAllComplete = false;
        // 自动化管理器开关
        private bool _autoManagerEnabled = false;
        #endregion

        #region OBS UI Updates
        private void OnObsConnected()
        {
            // 确保在 UI 线程执行
            Dispatcher.Invoke(() => UpdateObsStateUI(true));
        }

        private void OnObsDisconnected()
        {
            Dispatcher.Invoke(() => UpdateObsStateUI(false));
        }

        private void UpdateObsStateUI(bool connected)
        {
            try
            {
                if (txtOBSState == null) return;

                if (connected)
                {
                    txtOBSState.Text = "OBS连接： ● 已连接";
                    txtOBSState.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6AD37A"));
                }
                else
                {
                    txtOBSState.Text = "OBS连接： ● 未连接";
                    txtOBSState.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E15C4B"));
                }
            }
            catch { }
        }
        #endregion

        #region Constructor & Initialization
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

            // 订阅 OBS 连接/断开事件以更新 UI 状态
            ClsObs.OnConnected += OnObsConnected;
            ClsObs.OnDisconnected += OnObsDisconnected;

            // 根据当前连接状态初始化显示
            UpdateObsStateUI(ClsObs.IsConnected);

            //初始化KeyboardHook和F12取消机制
            try
            {
                _keyboardHook = new ClsKeyboardHook();
                _keyboardHook.Start();
                ClsGameControl.InitializeCancelToken(_keyboardHook);
                AppendLog("[信息] KeyboardHook 已初始化，F12取消功能可用");
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] KeyboardHook 初始化失败: {ex.Message}");
            }

            // 订阅 GotoScriptRace 事件
            ClsGameControl.BlueprintExecutionStarted += OnBlueprintExecutionStarted;
            ClsGameControl.PointCompletionCompleted += OnPointCompletionCompleted;
            // 订阅开始消耗点数事件（用于计时）
            ClsGameControl.UpCarPointBegin += OnUpCarPointBegin;
            ClsGameControl.BuyCarCompleted += OnBuyCarCompleted;

            //订阅蓝图脚本开启热键
            _keyboardHook.FunctionKeyPressed += _keyboardHook_FunctionKeyPressed;

            //订阅点数检测事件
            ClsGameControl.DetectPoint += ClsGameControl_DetectPoint;

            //订阅单个车辆完成事件和所有车辆完成事件
            ClsGameControl.SingelCarPointComplete += ClsGameControl_SingelCarPointComplete;
            ClsGameControl.AllCarPointComplete += ClsGameControl_AllCarPointComplete;
        }
        #endregion

        #region GameControl Event Handlers
        private void ClsGameControl_AllCarPointComplete(object? sender, EventArgs e)
        {
            UpCarIsAllComplete = true;
        }

        private void ClsGameControl_SingelCarPointComplete(object? sender, EventArgs e)
        {
            if (!UpCarIsAllComplete)
            {
                try
                {
                    string factory = txtCarFactory.Text;
                    string type = txtCarType.Text;
                    RunBackground(() => ClsGameControl.UpCarPoint(factory, type, false),
                        startLog: null,
                        cancelLog: "[信息] 消耗点数操作已取消",
                        errorPrefix: "消耗点数操作失败");
                }
                catch (Exception ex)
                {
                    AppendLog("[错误] 启动消耗点数失败: " + ex.Message);
                }
            }
        }

        private void ClsGameControl_DetectPoint(object? sender, int e)
        {
            var now = DateTime.Now;
            if (_lastPointUpdateTime.HasValue)
            {
                var interval = now - _lastPointUpdateTime.Value;
                if (interval > TimeSpan.Zero)
                {
                    _pointUpdateInterval = interval;
                }
            }

            _lastPointUpdateTime = now;
            _currentPoint = Math.Max(0, Math.Min(MaxPoint, e));

            txtPointTotal.Text = $"车辆热练度总数： {_currentPoint}";
            PrgPointTo999.Value = ((double)_currentPoint / MaxPoint) * 100;
            PrgPointToZero.Value = (1 - ((double)_currentPoint / MaxPoint)) * 100;

            UpdateEstimatedTime();
        }
        #endregion

        #region Auto Manager
        /// <summary>
        /// 自动化管理：接收各类事件并自动调用 ClsGameControl 执行动作
        /// 通过 F5 切换启用/禁用
        /// </summary>
        private void EnableAutoManager()
        {
            if (_autoManagerEnabled) return;
            _autoManagerEnabled = true;
            UpCarIsAllComplete = false;
            // 订阅事件
            ClsGameControl.SingelCarPointComplete += Auto_SingelCarPointComplete;
            ClsGameControl.AllCarPointComplete += Auto_AllCarPointComplete;
            ClsGameControl.PointCompletionCompleted += Auto_PointCompletionCompleted;
            ClsGameControl.BuyCarCompleted += Auto_BuyCarCompleted;
            ClsGameControl.BlueprintExecutionStarted += Auto_BlueprintExecutionStarted;
        }

        private void DisableAutoManager()
        {
            if (!_autoManagerEnabled) return;
            _autoManagerEnabled = false;
            try { ClsGameControl.SingelCarPointComplete -= Auto_SingelCarPointComplete; } catch { }
            try { ClsGameControl.AllCarPointComplete -= Auto_AllCarPointComplete; } catch { }
            try { ClsGameControl.PointCompletionCompleted -= Auto_PointCompletionCompleted; } catch { }
            try { ClsGameControl.BuyCarCompleted -= Auto_BuyCarCompleted; } catch { }
            try { ClsGameControl.BlueprintExecutionStarted -= Auto_BlueprintExecutionStarted; } catch { }
        }

        private void Auto_SingelCarPointComplete(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[自动] 单辆点数完成事件收到");
                if (!UpCarIsAllComplete && _autoManagerEnabled)
                {
                    string factory = txtCarFactory?.Text ?? string.Empty;
                    string type = txtCarType?.Text ?? string.Empty;
                    RunBackground(() => ClsGameControl.UpCarPoint(factory, type, false),
                        startLog: "[自动] 开始自动消耗点数",
                        cancelLog: "[自动] 自动消耗点数已取消",
                        errorPrefix: "自动消耗点数失败");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_SingelCarPointComplete: {ex.Message}");
            }
        }

        private void Auto_AllCarPointComplete(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[自动] 所有车辆点数完成事件收到");
                UpCarIsAllComplete = true;
                // 当所有车辆完成时，可选择停用自动管理器
                if (_autoManagerEnabled)
                {
                    AppendLog("[自动] 检测到所有车辆点数完成，自动化管理器将停止");
                    DisableAutoManager();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_AllCarPointComplete: {ex.Message}");
            }
        }

        private void Auto_PointCompletionCompleted(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[自动] 总点数达到完成阈值事件收到");
                // 当整体点数达到目标时，停止自动化并记录
                if (_autoManagerEnabled)
                {
                    AppendLog("[自动] 点数已达上限，自动化管理器已停止");
                    DisableAutoManager();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_PointCompletionCompleted: {ex.Message}");
            }
        }

        private void Auto_BuyCarCompleted(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[自动] 买车完成事件收到");
                // 可扩展：购买完成后执行下一步操作
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_BuyCarCompleted: {ex.Message}");
            }
        }

        private void Auto_BlueprintExecutionStarted(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[自动] 蓝图执行开始事件收到");
                // 可扩展：在蓝图开始时触发自动化首步
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_BlueprintExecutionStarted: {ex.Message}");
            }
        }
        #endregion

        #region Keyboard & Hotkeys
        private void _keyboardHook_FunctionKeyPressed(object? sender, FunctionKeyEventArgs e)
        {
            // F5: 切换自动化管理器
            if (e.Key == Key.F5)
            {
                try
                {
                    if (_autoManagerEnabled)
                    {
                        DisableAutoManager();
                        AppendLog("[信息] 自动化管理器已关闭");
                    }
                    else
                    {
                        EnableAutoManager();
                        AppendLog("[信息] 自动化管理器已启用");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[错误] 切换自动化管理器失败: {ex.Message}");
                }

                return;
            }

            if (e.Key == Key.F9)
            {
                AppendLog("[信息] 脚本赛车测试 按钮点击");
                AppendScriptLog("[测试] 启动 GotoScriptRace 测试");

                try
                {
                    string scriptCode = txtScriptCode.Text;
                    string factory = txtCarFactory.Text;
                    string type = txtCarType.Text;
                    int point = int.Parse(txtSinglePoint.Text);
                    RunBackground(() => ClsGameControl.GotoScriptRace(scriptCode, factory, type, point, debug: false),
                        startLog: null,
                        cancelLog: "[信息] 脚本赛车被用户取消",
                        errorPrefix: "脚本赛车执行失败");
                }
                catch (Exception ex)
                {
                    AppendLog("[错误] 启动脚本赛车失败: " + ex.Message);
                }
            }
            else if (e.Key == Key.F8)
            {
                // 与 lblGotoRace 一致的行为：按下 F8 开始消耗点数
                UpCarIsAllComplete = false;
                AppendPointLog($"[{DateTime.Now:HH:mm:ss}] 开始消耗点数...");
                try
                {
                    string factory = txtCarFactory.Text;
                    string type = txtCarType.Text;
                    RunBackground(() => ClsGameControl.UpCarPoint(factory, type, false),
                        startLog: null,
                        cancelLog: "[信息] 消耗点数操作已取消",
                        errorPrefix: "消耗点数操作失败");
                }
                catch (Exception ex)
                {
                    AppendLog("[错误] 启动消耗点数失败: " + ex.Message);
                }
            }
            else if (e.Key == Key.F7)
            {
                // F7: 从收集簿购买
                try
                {
                    int byCount = int.Parse(txtBuyCarNum.Text);
                    string factory = txtCarFactory.Text;
                    string type = txtCarType.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            ClsGameControl.BuyCar(byCount, factory, type, false);
                        }
                        catch (OperationCanceledException)
                        {
                            AppendLog("[信息] 买车操作已取消");
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[错误] 买车操作失败: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    AppendLog("[错误] 启动买车失败: " + ex.Message);
                }
            }
            else if (e.Key == Key.F6)
            {
                // F6: 从车库中移除
                try
                {
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] 开始移除车辆...");
                    try
                    {
                        string factory = txtCarFactory.Text;
                        string type = txtCarType.Text;
                        string score = txtCarScore?.Text ?? string.Empty;
                        RunBackground(() => ClsGameControl.DeleteCar(factory, type, score, false),
                            startLog: null,
                            cancelLog: "[信息] 移除车辆操作已取消",
                            errorPrefix: "移除车辆操作失败");
                    }
                    catch (Exception ex)
                    {
                        AppendLog("[错误] 启动移除车辆失败: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("[错误] 移除车辆失败: " + ex.Message);
                }
            }
        }
        #endregion

        #region Blueprint / Point Events
        /// <summary>
        /// 蓝图执行开始事件处理（用于启动定时器等）
        /// </summary>
        private void OnBlueprintExecutionStarted(object? sender, EventArgs e)
        {
            try
            {
                AppendScriptLog("[事件] 蓝图执行开始");
                // 启动 Race 计时
                _raceStartTime = DateTime.Now;
                _raceRunning = true;
                // 立即更新一次 UI
                UpdateRaceTime();
                UpdateEstimatedTime();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] OnBlueprintExecutionStarted: {ex.Message}");
            }
        }


        /// <summary>
        /// 点数完成事件处理（点数达到999）
        /// </summary>
        private void OnPointCompletionCompleted(object? sender, EventArgs e)
        {
            try
            {
                AppendPointLog("[完成] 点数已达到999！");
                AppendScriptLog("[事件] 蓝图执行完成 - 点数已达到999");
                _currentPoint = MaxPoint;
                // 停止计时并在UI上显示最终时长
                if (_raceRunning && _raceStartTime.HasValue)
                {
                    _raceRunning = false;
                    UpdateRaceTime();
                }

                if (_carPointRunning && _carPointStartTime.HasValue)
                {
                    _carPointRunning = false;
                    UpdateCarPointTime();
                }

                UpdateEstimatedTime();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] OnPointCompletionCompleted: {ex.Message}");
            }
        }

        /// <summary>
        /// 消耗点数开始事件处理（用于启动点数计时）
        /// </summary>
        private void OnUpCarPointBegin(object? sender, EventArgs e)
        {
            try
            {
                AppendPointLog($"[{DateTime.Now:HH:mm:ss}] 消耗点数开始事件触发，启动点数计时...");
                _carPointStartTime = DateTime.Now;
                _carPointRunning = true;
                UpdateCarPointTime();
                UpdateEstimatedTime();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] OnUpCarPointBegin: {ex.Message}");
            }
        }

        private void OnBuyCarCompleted(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[事件] 车辆购买已完成");
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] OnBuyCarCompleted: {ex.Message}");
            }
        }
        #endregion

        #region Timer & Time Updates
        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                UpdateCurrentTime();
                UpdateRunTime();
                // 更新蓝图运行时长与消耗点数时长显示
                if (_raceRunning) UpdateRaceTime();
                if (_carPointRunning) UpdateCarPointTime();
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

        // 更新蓝图/Race 计时显示到 txtRaceTime
        private void UpdateRaceTime()
        {
            try
            {
                if (txtRaceTime == null) return;
                if (!_raceStartTime.HasValue)
                {
                    txtRaceTime.Text = "已用时：00:00:00";
                    return;
                }

                var elapsed = DateTime.Now - _raceStartTime.Value;
                var hours = (int)elapsed.TotalHours;
                txtRaceTime.Text = $"已用时：{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }
            catch { }
        }

        // 更新消耗点数计时显示到 txtCarPointTime
        private void UpdateCarPointTime()
        {
            try
            {
                if (txtCarPointTime == null) return;
                if (!_carPointStartTime.HasValue)
                {
                    txtCarPointTime.Text = "已用时：00:00:00";
                    return;
                }

                var elapsed = DateTime.Now - _carPointStartTime.Value;
                var hours = (int)elapsed.TotalHours;
                txtCarPointTime.Text = $"已用时：{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }
            catch { }
        }

        private void UpdateEstimatedTime()
        {
            try
            {
                if (txtRaceEstimatedTime == null || txtCarPointEstimatedTime == null)
                {
                    return;
                }

                if (_currentPoint >= MaxPoint)
                {
                    txtRaceEstimatedTime.Text = "预计用时：00:00:00";
                    txtCarPointEstimatedTime.Text = "预计用时：00:00:00";
                    return;
                }

                if (!_pointUpdateInterval.HasValue || _pointUpdateInterval.Value <= TimeSpan.Zero)
                {
                    txtRaceEstimatedTime.Text = "预计用时：--:--:--";
                    txtCarPointEstimatedTime.Text = "预计用时：--:--:--";
                    return;
                }

                var leftPoint = MaxPoint - _currentPoint;
                var interval = _pointUpdateInterval.Value;

                if (_raceRunning)
                {
                    var singlePoint = ParseSingleRacePoint();
                    var raceEstimated = TimeSpan.FromTicks((long)((leftPoint / (double)singlePoint) * interval.Ticks));
                    txtRaceEstimatedTime.Text = $"预计用时：{FormatTimeSpan(raceEstimated)}";
                    txtCarPointEstimatedTime.Text = "预计用时：--:--:--";
                    return;
                }

                if (_carPointRunning)
                {
                    var pointEstimated = TimeSpan.FromTicks((long)((leftPoint / (double)PointModeSingleLoopPoint) * interval.Ticks));
                    txtCarPointEstimatedTime.Text = $"预计用时：{FormatTimeSpan(pointEstimated)}";
                    txtRaceEstimatedTime.Text = "预计用时：--:--:--";
                    return;
                }

                txtRaceEstimatedTime.Text = "预计用时：--:--:--";
                txtCarPointEstimatedTime.Text = "预计用时：--:--:--";
            }
            catch
            {
            }
        }

        private int ParseSingleRacePoint()
        {
            if (!int.TryParse(txtSinglePoint?.Text, out var singlePoint) || singlePoint <= 0)
            {
                return 1;
            }

            return singlePoint;
        }

        private static string FormatTimeSpan(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            var hours = (int)value.TotalHours;
            return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
        #endregion

        #region Window Events & UI Actions
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
            UpCarIsAllComplete = false;
            AppendLog("[信息] 消耗技能点脚本测试 按钮点击");
            AppendPointLog("[测试] 启动 UpCarPoint 测试");

            try
            {
                string factory = txtCarFactory.Text;
                string type = txtCarType.Text;
                RunBackground(() => ClsGameControl.UpCarPoint(factory, type, true),
                    startLog: null,
                    cancelLog: "[信息] 消耗技能点脚本测试",
                    errorPrefix: "消耗技能点脚本测试");
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 消耗技能点脚本测试: " + ex.Message);
            }
        }

        private void btnTestBycar_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] 买车脚本测试 按钮点击");
            AppendLog("[测试] 启动 BuyCar 测试");

            try
            {
                int byCount = int.Parse(txtBuyCarNum.Text);
                string factory = txtCarFactory.Text;
                string type = txtCarType.Text;

                RunBackground(() => ClsGameControl.BuyCar(byCount, factory, type, true),
                    startLog: null,
                    cancelLog: "[信息] 买车脚本测试",
                    errorPrefix: "买车脚本测试");
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 买车脚本测试: " + ex.Message);
            }
        }



        private void btnROI_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] ROI 按钮点击");
            //取图
            var sources = ClsObs._obs.GetCurrentProgramScene();
            string? base64Image = ClsObs.GetSourceScreenshotAsync(sources, "png", ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight, 100).GetAwaiter().GetResult();
            byte[] gameShot = Convert.FromBase64String(base64Image);
            var imageMat = Cv2.ImDecode(gameShot, ImreadModes.Color);

            //获取游戏窗口大小
            ClsGameControl.TryGetWindowRectByProcessName("ForzaHorizon6", out var wndRect);

            //选择ROI并保存
            ClsROI.SelectAndAssignROI(imageMat, wndRect, ClsROI.TargetRects, out ClsROI.UIElem? assignedKey);
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 注销 GotoScriptRace 事件
                ClsGameControl.BlueprintExecutionStarted -= OnBlueprintExecutionStarted;
                ClsGameControl.PointCompletionCompleted -= OnPointCompletionCompleted;
                ClsGameControl.UpCarPointBegin -= OnUpCarPointBegin;
                ClsGameControl.BuyCarCompleted -= OnBuyCarCompleted;
                ClsGameControl.DetectPoint -= ClsGameControl_DetectPoint;
                ClsGameControl.SingelCarPointComplete -= ClsGameControl_SingelCarPointComplete;
                ClsGameControl.AllCarPointComplete -= ClsGameControl_AllCarPointComplete;

                // 确保自动化管理器被关闭并反订阅
                try
                {
                    DisableAutoManager();
                }
                catch { }

                if (_keyboardHook != null)
                {
                    _keyboardHook.Stop();
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                }
                ClsGameControl.DisposeCancelToken();
                // 取消订阅 OBS 事件
                try { ClsObs.OnConnected -= OnObsConnected; } catch { }
                try { ClsObs.OnDisconnected -= OnObsDisconnected; } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Window_Closing error: {ex.Message}");
            }
        }
        #endregion

        #region Logging Helpers
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
        #endregion

        #region Background Runner
        /// <summary>
        /// 在后台线程运行指定操作并统一处理取消与异常日志
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="startLog">操作开始时写入的日志（可为 null）</param>
        /// <param name="cancelLog">取消时写入的日志（可为 null）</param>
        /// <param name="errorPrefix">异常时前缀说明（可为 null）</param>
        private void RunBackground(Action action, string? startLog = null, string? cancelLog = null, string? errorPrefix = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(startLog)) AppendLog(startLog);
                Task.Run(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (OperationCanceledException)
                    {
                        if (!string.IsNullOrEmpty(cancelLog)) AppendLog(cancelLog);
                    }
                    catch (Exception ex)
                    {
                        if (!string.IsNullOrEmpty(errorPrefix))
                            AppendLog($"[错误] {errorPrefix}: {ex.Message}");
                        else
                            AppendLog($"[错误] 后台任务失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog("[错误] 启动后台任务失败: " + ex.Message);
            }
        }
        #endregion

    }
}
