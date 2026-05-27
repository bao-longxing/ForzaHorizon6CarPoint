using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace FH_WPF
{
    public partial class MainWindow : System.Windows.Window
    {
        #region 字段
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
        private int? _previousPoint = null;
        private const int MaxPoint = 999;
        private const int PointModeSingleLoopPoint = 30;
        private ClsKeyboardHook? _keyboardHook;
        private bool UpCarIsAllComplete = false;

        private enum AutoManagerState
        {
            Idle,
            ScriptRaceRunning,
            BuyCarRunning,
            UpCarPointRunning,
            DeleteCarRunning
        }

        // 自动化管理器状态机
        private bool _autoManagerEnabled = false;
        private AutoManagerState _autoManagerState = AutoManagerState.Idle;
        private readonly object _autoManagerLock = new();
        private CancellationTokenSource? _upvoteMonitorCts;
        #endregion

        #region OBS 界面更新
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

        // 兼容 XAML 中的命名处理器
        private void btnOBSSettings_Click(object sender, RoutedEventArgs e)
        {
            Button_Click(sender, e);
        }
        #endregion

        #region 构造函数与初始化
        public MainWindow()
        {
            InitializeComponent();

            // 加载持久化的界面数据
            try { LoadSettings(); } catch { }
            _startTime = DateTime.Now;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            //初始化日志（统一输出到日志列表）
            ClsLogger.Init();
            lvLog.ItemsSource = ClsLogger.Entries;

            //初始化OBS
            // 延后连接 OBS，使用持久化的配置（如果存在）
            try
            {
                if (!string.IsNullOrWhiteSpace(_obsIp) && _obsPort > 0)
                {
                    // 捕获连接结果并立即更新 UI，避免仅依赖事件回调导致状态不同步的情况
                    try
                    {
                        var ok = ClsObs.ConnectAsync(_obsIp, _obsPort, _obsPassword ?? string.Empty).GetAwaiter().GetResult();
                        AppendLog(ok ? "[信息] OBS 已按配置初始化并已连接" : "[信息] OBS 已按配置初始化但未连接");
                        UpdateObsStateUI(ok);
                    }
                    catch (Exception exInner)
                    {
                        AppendLog($"[错误] OBS 初始化失败: {exInner.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] OBS 初始化失败: {ex.Message}");
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
                AppendLog("[信息] KeyboardHook 已初始化，F11取消功能可用");
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
            ClsGameControl.AllCarPointComplete += ClsGameControl_AllCarPointComplete;
        }

        #region 设置持久化
        private class UISettings
        {
            public string? txtCarScore { get; set; }
            public string? txtCarFactory { get; set; }
            public string? txtCarType { get; set; }
            public string? txtBuyCarNum { get; set; }
            public string? txtScriptCode { get; set; }
            public string? txtSinglePoint { get; set; }
            public string? ObsIp { get; set; }
            public int? ObsPort { get; set; }
            public string? ObsPassword { get; set; }
        }

        // 持久化的 OBS 配置缓存
        private string? _obsIp;
        private int _obsPort;
        private string? _obsPassword;

        private string GetSettingsPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FH_WPF");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        private void LoadSettings()
        {
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<UISettings>(json, opts);
                if (s == null) return;
                Dispatcher.Invoke(() =>
                {
                    try { if (txtCarScore != null && s.txtCarScore != null) txtCarScore.Text = s.txtCarScore; } catch { }
                    try { if (txtCarFactory != null && s.txtCarFactory != null) txtCarFactory.Text = s.txtCarFactory; } catch { }
                    try { if (txtCarType != null && s.txtCarType != null) txtCarType.Text = s.txtCarType; } catch { }
                    try { if (txtBuyCarNum != null && s.txtBuyCarNum != null) txtBuyCarNum.Text = s.txtBuyCarNum; } catch { }
                    try { if (txtScriptCode != null && s.txtScriptCode != null) txtScriptCode.Text = s.txtScriptCode; } catch { }
                    try { if (txtSinglePoint != null && s.txtSinglePoint != null) txtSinglePoint.Text = s.txtSinglePoint; } catch { }
                });

                // 读取 OBS 配置到内存
                try { _obsIp = s.ObsIp; } catch { }
                try { if (s.ObsPort.HasValue) _obsPort = s.ObsPort.Value; } catch { }
                try { _obsPassword = s.ObsPassword; } catch { }
            }
            catch
            {
                // ignore load errors
            }
        }

        private void SaveSettings()
        {
            try
            {
                var s = new UISettings();
                try { s.txtCarScore = txtCarScore?.Text; } catch { }
                try { s.txtCarFactory = txtCarFactory?.Text; } catch { }
                try { s.txtCarType = txtCarType?.Text; } catch { }
                try { s.txtBuyCarNum = txtBuyCarNum?.Text; } catch { }
                try { s.txtScriptCode = txtScriptCode?.Text; } catch { }
                try { s.txtSinglePoint = txtSinglePoint?.Text; } catch { }
                try { s.ObsIp = _obsIp; } catch { }
                try { s.ObsPort = _obsPort; } catch { }
                try { s.ObsPassword = _obsPassword; } catch { }

                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(s, opts);
                var path = GetSettingsPath();
                File.WriteAllText(path, json);
            }
            catch
            {
                // ignore save errors
            }
        }
        #endregion
        #endregion

        #region GameControl 事件处理
        private void ClsGameControl_AllCarPointComplete(object? sender, EventArgs e)
        {
            UpCarIsAllComplete = true;
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
            // 在更新前记录之前的点数
            _previousPoint = _currentPoint;
            _currentPoint = Math.Max(0, Math.Min(MaxPoint, e));

            txtPointTotal.Text = $"车辆热练度总数： {_currentPoint}";
            var progressToMax = ((double)_currentPoint / MaxPoint) * 100;
            var progressToZero = (1 - ((double)_currentPoint / MaxPoint)) * 100;
            var unifiedProgress = _carPointRunning ? progressToZero : progressToMax;
            PrgPointTo999.Value = unifiedProgress;
            PrgPointToZero.Value = unifiedProgress;

            UpdateEstimatedTime();
        }
        #endregion

        #region 自动化管理器
        /// <summary>
        /// 启用自动化状态机：
        /// 蓝图开始 -> 蓝图结束(needRepeat?重试:进入买车) -> 买车完成 -> 消耗开始/结束 -> 移除完成 -> 回到蓝图
        /// 并启动长期点赞检测。
        /// </summary>
        private void EnableAutoManager()
        {
            lock (_autoManagerLock)
            {
                if (_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerEnabled = true;
                _autoManagerState = AutoManagerState.Idle;
                UpCarIsAllComplete = false;
            }

            ClsGameControl.BlueprintExecutionStarted += Auto_BlueprintExecutionStarted;
            ClsGameControl.PointCompletionCompleted += Auto_PointCompletionCompleted;
            ClsGameControl.BuyCarCompleted += Auto_BuyCarCompleted;
            ClsGameControl.UpCarPointBegin += Auto_UpCarPointBegin;
            ClsGameControl.AllCarPointComplete += Auto_AllCarPointComplete;
            ClsGameControl.DeleteCarCompleted += Auto_DeleteCarCompleted;

            _upvoteMonitorCts = new CancellationTokenSource();
            _ = Task.Run(() => AutoMonitorUpvoteLoop(_upvoteMonitorCts.Token));

            AppendLog("[自动] 自动化状态机已启用");
            StartScriptRaceFromStateMachine();
        }

        /// <summary>
        /// 关闭自动化状态机并取消后台点赞检测，同时解除所有自动化事件订阅。
        /// </summary>
        private void DisableAutoManager()
        {
            CancellationTokenSource? ctsToCancel = null;

            lock (_autoManagerLock)
            {
                if (!_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerEnabled = false;
                _autoManagerState = AutoManagerState.Idle;
                ctsToCancel = _upvoteMonitorCts;
                _upvoteMonitorCts = null;
            }

            try { ctsToCancel?.Cancel(); } catch { }
            try { ctsToCancel?.Dispose(); } catch { }

            try { ClsGameControl.BlueprintExecutionStarted -= Auto_BlueprintExecutionStarted; } catch { }
            try { ClsGameControl.PointCompletionCompleted -= Auto_PointCompletionCompleted; } catch { }
            try { ClsGameControl.BuyCarCompleted -= Auto_BuyCarCompleted; } catch { }
            try { ClsGameControl.UpCarPointBegin -= Auto_UpCarPointBegin; } catch { }
            try { ClsGameControl.AllCarPointComplete -= Auto_AllCarPointComplete; } catch { }
            try { ClsGameControl.DeleteCarCompleted -= Auto_DeleteCarCompleted; } catch { }

            AppendLog("[自动] 自动化状态机已关闭");
        }

        /// <summary>
        /// 处理蓝图执行开始事件，并同步自动化状态为蓝图运行中。
        /// </summary>
        private void Auto_BlueprintExecutionStarted(object? sender, EventArgs e)
        {
            try
            {
                lock (_autoManagerLock)
                {
                    if (!_autoManagerEnabled)
                    {
                        return;
                    }

                    _autoManagerState = AutoManagerState.ScriptRaceRunning;
                }

                AppendLog("[自动] 状态 -> ScriptRaceRunning（蓝图执行开始）");
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_BlueprintExecutionStarted: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理蓝图完成事件：根据是否需要重试决定重跑蓝图或进入买车流程。
        /// </summary>
        private void Auto_PointCompletionCompleted(object? sender, bool needRepeat)
        {
            try
            {
                bool enabled;
                lock (_autoManagerLock)
                {
                    enabled = _autoManagerEnabled;
                }

                if (!enabled)
                {
                    return;
                }

                if (needRepeat)
                {
                    AppendLog("[自动] 蓝图结束，needRepeat=true，重试蓝图流程");
                    StartScriptRaceFromStateMachine();
                    return;
                }

                AppendLog("[自动] 蓝图结束，进入买车流程");
                StartBuyCarFromStateMachine();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_PointCompletionCompleted: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理买车完成事件，并推进到消耗点数流程。
        /// </summary>
        private void Auto_BuyCarCompleted(object? sender, EventArgs e)
        {
            try
            {
                bool enabled;
                lock (_autoManagerLock)
                {
                    enabled = _autoManagerEnabled;
                }

                if (!enabled)
                {
                    return;
                }

                AppendLog("[自动] 买车完成，进入消耗点数流程");
                StartUpCarPointFromStateMachine();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_BuyCarCompleted: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理消耗点数开始事件，并更新自动化状态。
        /// </summary>
        private void Auto_UpCarPointBegin(object? sender, EventArgs e)
        {
            try
            {
                lock (_autoManagerLock)
                {
                    if (!_autoManagerEnabled)
                    {
                        return;
                    }

                    _autoManagerState = AutoManagerState.UpCarPointRunning;
                }

                AppendLog("[自动] 状态 -> UpCarPointRunning（开始消耗点数）");
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_UpCarPointBegin: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理全部车辆点数消耗完成事件，并进入移除车辆流程。
        /// </summary>
        private void Auto_AllCarPointComplete(object? sender, EventArgs e)
        {
            try
            {
                bool enabled;
                lock (_autoManagerLock)
                {
                    enabled = _autoManagerEnabled;
                }

                if (!enabled)
                {
                    return;
                }

                UpCarIsAllComplete = true;
                AppendLog("[自动] 消耗点数完成，进入移除流程");
                StartDeleteCarFromStateMachine();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_AllCarPointComplete: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理移除车辆完成事件，并回到蓝图流程继续循环。
        /// </summary>
        private void Auto_DeleteCarCompleted(object? sender, EventArgs e)
        {
            try
            {
                bool enabled;
                lock (_autoManagerLock)
                {
                    enabled = _autoManagerEnabled;
                }

                if (!enabled)
                {
                    return;
                }

                AppendLog("[自动] 移除完成，回到蓝图流程");
                StartScriptRaceFromStateMachine();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] Auto_DeleteCarCompleted: {ex.Message}");
            }
        }

        /// <summary>
        /// 从状态机触发蓝图脚本流程并切换到蓝图运行状态。
        /// </summary>
        private void StartScriptRaceFromStateMachine()
        {
            string scriptCode = string.Empty;
            string factory = string.Empty;
            string type = string.Empty;
            int point = 1;

            Dispatcher.Invoke(() =>
            {
                scriptCode = txtScriptCode.Text;
                factory = txtCarFactory.Text;
                type = txtCarType.Text;
                point = ParseSingleRacePoint();
            });

            lock (_autoManagerLock)
            {
                if (!_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerState = AutoManagerState.ScriptRaceRunning;
            }

            RunBackground(() => ClsGameControl.GotoScriptRace(scriptCode, factory, type, point, debug: false),
                startLog: "[自动] 启动蓝图流程",
                cancelLog: "[自动] 蓝图流程已取消",
                errorPrefix: "自动蓝图流程失败");
        }

        /// <summary>
        /// 从状态机触发买车流程并切换到买车运行状态。
        /// </summary>
        private void StartBuyCarFromStateMachine()
        {
            int buyCount = 1;
            string factory = string.Empty;
            string type = string.Empty;

            Dispatcher.Invoke(() =>
            {
                buyCount = int.TryParse(txtBuyCarNum.Text, out var parsedCount) && parsedCount > 0 ? parsedCount : 1;
                factory = txtCarFactory.Text;
                type = txtCarType.Text;
            });

            lock (_autoManagerLock)
            {
                if (!_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerState = AutoManagerState.BuyCarRunning;
            }

            RunBackground(() => ClsGameControl.BuyCar(buyCount, factory, type, false),
                startLog: "[自动] 启动买车流程",
                cancelLog: "[自动] 买车流程已取消",
                errorPrefix: "自动买车流程失败");
        }

        /// <summary>
        /// 从状态机触发消耗点数流程并切换到点数消耗状态。
        /// </summary>
        private void StartUpCarPointFromStateMachine()
        {
            string factory = string.Empty;
            string type = string.Empty;

            Dispatcher.Invoke(() =>
            {
                factory = txtCarFactory.Text;
                type = txtCarType.Text;
            });

            lock (_autoManagerLock)
            {
                if (!_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerState = AutoManagerState.UpCarPointRunning;
            }

            RunBackground(() => ClsGameControl.UpCarPoint(factory, type, false),
                startLog: "[自动] 启动消耗点数流程",
                cancelLog: "[自动] 消耗点数流程已取消",
                errorPrefix: "自动消耗点数流程失败");
        }

        /// <summary>
        /// 从状态机触发移除车辆流程并切换到移除运行状态。
        /// </summary>
        private void StartDeleteCarFromStateMachine()
        {
            string factory = string.Empty;
            string type = string.Empty;
            string score = string.Empty;

            Dispatcher.Invoke(() =>
            {
                factory = txtCarFactory.Text;
                type = txtCarType.Text;
                score = txtCarScore?.Text ?? string.Empty;
            });

            lock (_autoManagerLock)
            {
                if (!_autoManagerEnabled)
                {
                    return;
                }

                _autoManagerState = AutoManagerState.DeleteCarRunning;
            }

            RunBackground(() => ClsGameControl.DeleteCar(factory, type, score, false),
                startLog: "[自动] 启动移除流程",
                cancelLog: "[自动] 移除流程已取消",
                errorPrefix: "自动移除流程失败");
        }

        /// <summary>
        /// 在后台循环检测点赞入口，直到自动化关闭或收到取消信号。
        /// </summary>
        private async Task AutoMonitorUpvoteLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    bool enabled;
                    lock (_autoManagerLock)
                    {
                        enabled = _autoManagerEnabled;
                    }

                    if (!enabled)
                    {
                        return;
                    }

                    bool upvoteOk = ClsGameControl.CheckUpvote();
                    if (upvoteOk)
                    {
                        AppendLog("[自动] 检测到点赞并已执行");
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppendLog($"[错误] 自动点赞检测失败: {ex.Message}");
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        #endregion

        #region 键盘与热键
        private void _keyboardHook_FunctionKeyPressed(object? sender, FunctionKeyEventArgs e)
        {
            // F11: 强制停止自动化状态机
            if (e.Key == Key.F11)
            {
                try
                {
                    DisableAutoManager();
                    AppendLog("[信息] 自动化管理器已被强制停止 (F11)");
                }
                catch (Exception ex)
                {
                    AppendLog($"[错误] 使用 F11 停止自动化管理器失败: {ex.Message}");
                }

                return;
            }

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
                // F9: 蓝图脚本
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
            else if (e.Key == Key.F7)
            {
                // F7: 与 lblPoint 一致的行为：开始消耗点数
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
            else if (e.Key == Key.F6)
            {
                // F6: 从收集簿购买
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
            else if (e.Key == Key.F8)
            {
                // F8: 从车库中移除
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

        #region 蓝图与点数事件
        /// <summary>
        /// 蓝图执行开始事件处理（用于启动定时器等）
        /// </summary>
        private void OnBlueprintExecutionStarted(object? sender, EventArgs e)
        {
            try
            {
                AppendScriptLog("[事件] 蓝图执行开始");
                // 切换为蓝图模式
                _carPointRunning = false;
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
        private void OnPointCompletionCompleted(object? sender, bool needRepeat)
        {
            try
            {
                AppendScriptLog("[事件] 蓝图执行完成");
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
                // 切换为点数消耗模式
                _raceRunning = false;
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

        #region 定时器与时间更新
        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                UpdateCurrentTime();
                UpdateRunTime();
                // 更新蓝图运行时长与消耗点数时长显示
                if (_raceRunning) UpdateRaceTime();
                if (_carPointRunning) UpdateCarPointTime();
                // 每秒更新预计剩余时间，使其按秒递减
                UpdateEstimatedTime();
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

        // 更新消耗点数计时显示到统一时间控件
        private void UpdateCarPointTime()
        {
            try
            {
                if (txtRaceTime == null || txtCarPointTime == null) return;
                if (!_carPointStartTime.HasValue)
                {
                    txtRaceTime.Text = "已用时：00:00:00";
                    txtCarPointTime.Text = "已用时：00:00:00";
                    return;
                }

                var elapsed = DateTime.Now - _carPointStartTime.Value;
                var hours = (int)elapsed.TotalHours;
                var value = $"已用时：{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                txtRaceTime.Text = value;
                txtCarPointTime.Text = value;
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
                // 计算自上次点数更新以来已过去的时间，用于让预计时间随时间减少
                var elapsedSinceLastPoint = TimeSpan.Zero;
                if (_lastPointUpdateTime.HasValue)
                {
                    elapsedSinceLastPoint = DateTime.Now - _lastPointUpdateTime.Value;
                    if (elapsedSinceLastPoint < TimeSpan.Zero) elapsedSinceLastPoint = TimeSpan.Zero;
                }

                if (_raceRunning)
                {
                    // 优先使用观测到的每次更新的增加量（delta）进行估算（如可用）
                    if (_previousPoint.HasValue)
                    {
                        var delta = _currentPoint - _previousPoint.Value; // positive if points are increasing per update
                        if (delta > 0)
                        {
                            var leftToMax = MaxPoint - _currentPoint;
                            var intervalsNeeded = leftToMax / (double)delta;
                            var raceTicksByDelta = (long)(intervalsNeeded * interval.Ticks);
                            var raceRemainingTicksByDelta = raceTicksByDelta - elapsedSinceLastPoint.Ticks;
                            if (raceRemainingTicksByDelta < 0) raceRemainingTicksByDelta = 0;
                            var raceEstimatedFromDelta = TimeSpan.FromTicks(raceRemainingTicksByDelta);
                            var value = $"预计用时：{FormatTimeSpan(raceEstimatedFromDelta)}";
                            txtRaceEstimatedTime.Text = value;
                            txtCarPointEstimatedTime.Text = value;
                            return;
                        }
                        // 如果 delta <= 0，则回退到使用配置的单圈点数估算
                    }

                    // 回退到使用配置的单圈点数估算
                    var singlePoint = ParseSingleRacePoint();
                    var raceTicksBySinglePoint = (long)((leftPoint / (double)singlePoint) * interval.Ticks);
                    var raceRemainingTicksBySinglePoint = raceTicksBySinglePoint - elapsedSinceLastPoint.Ticks;
                    if (raceRemainingTicksBySinglePoint < 0) raceRemainingTicksBySinglePoint = 0;
                    var raceEstimatedFromSinglePoint = TimeSpan.FromTicks(raceRemainingTicksBySinglePoint);
                    var fallbackValue = $"预计用时：{FormatTimeSpan(raceEstimatedFromSinglePoint)}";
                    txtRaceEstimatedTime.Text = fallbackValue;
                    txtCarPointEstimatedTime.Text = fallbackValue;
                    return;
                }

                if (_carPointRunning)
                {
                    // 使用最近两次点数读取差值来估算点数降至阈值（PointModeSingleLoopPoint）所需时间
                    if (_previousPoint.HasValue)
                    {
                        var delta = _previousPoint.Value - _currentPoint; // positive if points are decreasing
                        if (delta > 0)
                        {
                            var leftToThreshold = _currentPoint - PointModeSingleLoopPoint;
                            if (leftToThreshold <= 0)
                            {
                                txtCarPointEstimatedTime.Text = "预计用时：00:00:00";
                                txtRaceEstimatedTime.Text = "预计用时：00:00:00";
                                return;
                            }

                            // 需要多少个间隔：leftToThreshold / delta
                            var intervalsNeeded = leftToThreshold / (double)delta;
                            var carPointTotalRemainingTicks = (long)(intervalsNeeded * interval.Ticks);
                            var carPointRemainingTicks = carPointTotalRemainingTicks - elapsedSinceLastPoint.Ticks;
                            if (carPointRemainingTicks < 0) carPointRemainingTicks = 0;
                            var carPointEstimatedTimeSpan = TimeSpan.FromTicks(carPointRemainingTicks);
                            var value = $"预计用时：{FormatTimeSpan(carPointEstimatedTimeSpan)}";
                            txtCarPointEstimatedTime.Text = value;
                            txtRaceEstimatedTime.Text = value;
                            return;
                        }
                        else
                        {
                            // 未观测到下降，无法估算
                            txtCarPointEstimatedTime.Text = "预计用时：--:--:--";
                            txtRaceEstimatedTime.Text = "预计用时：--:--:--";
                            return;
                        }
                    }
                    else
                    {
                        // 无可用的之前点数，无法估算
                        txtCarPointEstimatedTime.Text = "预计用时：--:--:--";
                        txtRaceEstimatedTime.Text = "预计用时：--:--:--";
                        return;
                    }
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

        #region 窗口事件与界面操作
        // Win32 constants for hit testing to enable resizing on a borderless window
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                hwndSource?.AddHook(WndProc);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                try
                {
                    // extract screen coordinates from lParam
                    int lParamInt = lParam.ToInt32();
                    int x = (short)(lParamInt & 0xFFFF);
                    int y = (short)((lParamInt >> 16) & 0xFFFF);

                    // convert to window-relative coordinates
                    var ptScreen = new System.Windows.Point(x, y);
                    var ptWindow = this.PointFromScreen(ptScreen);

                    const int resizeBorder = 8; // thickness in pixels
                    double width = this.ActualWidth;
                    double height = this.ActualHeight;

                    // top
                    if (ptWindow.Y >= 0 && ptWindow.Y <= resizeBorder)
                    {
                        if (ptWindow.X <= resizeBorder)
                        {
                            handled = true; return new IntPtr(HTTOPLEFT);
                        }
                        if (ptWindow.X >= width - resizeBorder)
                        {
                            handled = true; return new IntPtr(HTTOPRIGHT);
                        }
                        handled = true; return new IntPtr(HTTOP);
                    }

                    // bottom
                    if (ptWindow.Y >= height - resizeBorder && ptWindow.Y <= height)
                    {
                        if (ptWindow.X <= resizeBorder)
                        {
                            handled = true; return new IntPtr(HTBOTTOMLEFT);
                        }
                        if (ptWindow.X >= width - resizeBorder)
                        {
                            handled = true; return new IntPtr(HTBOTTOMRIGHT);
                        }
                        handled = true; return new IntPtr(HTBOTTOM);
                    }

                    // left
                    if (ptWindow.X >= 0 && ptWindow.X <= resizeBorder)
                    {
                        handled = true; return new IntPtr(HTLEFT);
                    }

                    // right
                    if (ptWindow.X >= width - resizeBorder && ptWindow.X <= width)
                    {
                        handled = true; return new IntPtr(HTRIGHT);
                    }
                }
                catch { }
            }

            return IntPtr.Zero;
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





        private void Button_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] OBS 按钮点击");
            try
            {
                var dlg = new ObsSettingsWindow(_obsIp, _obsPort, _obsPassword);
                dlg.Owner = this;
                var res = dlg.ShowDialog();
                if (res == true)
                {
                    // 保存并连接
                    _obsIp = dlg.ObsIp;
                    _obsPort = dlg.ObsPort;
                    _obsPassword = dlg.ObsPassword;
                    SaveSettings();

                    // 尝试连接
                    Task.Run(async () =>
                    {
                        try
                        {
                            var ok = await ClsObs.ConnectAsync(_obsIp!, _obsPort, _obsPassword ?? string.Empty);
                            Dispatcher.Invoke(() =>
                            {
                                AppendLog(ok ? "[信息] OBS 连接成功" : "[信息] OBS 连接失败");
                                try { UpdateObsStateUI(ok); } catch { }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AppendLog($"[错误] 连接 OBS 失败: {ex.Message}"));
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 打开 OBS 设置失败: {ex.Message}");
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] KeyBoard 按钮点击");
        }

        private void btnOCRTest_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("[信息] OCR 测试 按钮点击");
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
                // 保存界面可编辑控件内容
                try { SaveSettings(); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Window_Closing error: {ex.Message}");
            }
        }
        #endregion

        #region 日志辅助
        private void AppendLog(string message)
        {
            ClsLogger.Log(message);
            // 自动滚动到最新条目
            try
            {
                if (lvLog.Items.Count > 0)
                    lvLog.ScrollIntoView(lvLog.Items[lvLog.Items.Count - 1]);
            }
            catch { }
        }

        private void AppendPointLog(string message)
        {
            ClsLogger.LogPoint(message);
            try
            {
                if (lvLog.Items.Count > 0)
                    lvLog.ScrollIntoView(lvLog.Items[lvLog.Items.Count - 1]);
            }
            catch { }
        }

        private void AppendScriptLog(string message)
        {
            ClsLogger.LogScript(message);
            try
            {
                if (lvLog.Items.Count > 0)
                    lvLog.ScrollIntoView(lvLog.Items[lvLog.Items.Count - 1]);
            }
            catch { }
        }
        #endregion

        #region 后台运行器
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
