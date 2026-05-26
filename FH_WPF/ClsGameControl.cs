using OpenCvSharp;
using Sdcb.PaddleOCR;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace FH_WPF
{
    internal static class ClsGameControl
    {
        #region 事件定义
        /// <summary>
        /// 蓝图执行开始事件（用于定时）
        /// </summary>
        public static event EventHandler? BlueprintExecutionStarted;

        /// <summary>
        /// 点数完成事件（点数达到或超过999）
        /// </summary>
        public static event EventHandler? PointCompletionCompleted;

        /// <summary>
        /// 消耗点数开始事件（用于定时）
        /// </summary>
        public static event EventHandler? UpCarPointBegin;

        /// <summary>
        /// 点数检测事件
        /// </summary>
        public static event EventHandler<int>? DetectPoint;

        /// <summary>
        /// 购买完成事件
        /// </summary>
        public static event EventHandler? BuyCarCompleted;

        /// <summary>
        /// 删除完成事件
        /// </summary>
        public static event EventHandler? DeleteCarCompleted;

        /// <summary>
        /// 单个车辆点数完成事件
        /// </summary>
        public static event EventHandler? SingelCarPointComplete;

        /// <summary>
        /// 所有车辆点数完成事件
        /// </summary>
        public static event EventHandler? AllCarPointComplete;

        // 事件的直接触发请在实际流程中使用
        #endregion

        #region CancelToken机制
        private static CancellationTokenSource? _cancelTokenSource;
        // 锁用于线程安全地重建或替换 CancellationTokenSource
        private static readonly object _cancelTokenLock = new object();

        /// <summary>
        /// 如果当前的 CancellationTokenSource 为 null 或已被取消，则重建它以便后续高级操作可重用。
        /// 注意：键盘钩子订阅在 InitializeCancelToken 中完成，这里仅负责（重）创建 TokenSource 实例。
        /// </summary>
        private static void RebuildCancelTokenIfNeeded()
        {
            lock (_cancelTokenLock)
            {
                try
                {
                    if (_cancelTokenSource == null)
                    {
                        _cancelTokenSource = new CancellationTokenSource();
                        return;
                    }

                    if (_cancelTokenSource.IsCancellationRequested)
                    {
                        try { _cancelTokenSource.Dispose(); } catch { }
                        _cancelTokenSource = new CancellationTokenSource();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RebuildCancelTokenIfNeeded 异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取当前操作的取消令牌。可由高级操作使用以支持F11取消。
        /// 如果未初始化，返回CancellationToken.None。
        /// </summary>
        public static CancellationToken CancellationToken
        {
            get
            {
                if (_cancelTokenSource == null)
                {
                    return CancellationToken.None;
                }
                return _cancelTokenSource.Token;
            }
        }

        /// <summary>
        /// 初始化F11取消机制。需要在应用启动时调用，并传入KeyboardHook实例。
        /// </summary>
        /// <param name="keyboardHook">KeyboardHook实例，用于监听F11按键。</param>
        public static void InitializeCancelToken(ClsKeyboardHook keyboardHook)
        {
            if (keyboardHook == null)
            {
                Debug.WriteLine("ClsGameControl.InitializeCancelToken: keyboardHook为null，取消初始化");
                return;
            }

            // 线程安全地创建 CancellationTokenSource（或重建）
            lock (_cancelTokenLock)
            {
                try
                {
                    if (_cancelTokenSource == null || _cancelTokenSource.IsCancellationRequested)
                    {
                        try { _cancelTokenSource?.Dispose(); } catch { }
                        _cancelTokenSource = new CancellationTokenSource();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"InitializeCancelToken 创建 CancellationTokenSource 异常: {ex.Message}");
                }
            }

            // 订阅 F11 按键事件（允许重复订阅导致多次触发，因此先尝试移除再添加）
            try
            {
                keyboardHook.FunctionKeyPressed -= KeyboardHook_FunctionKeyPressed;
            }
            catch { }
            keyboardHook.FunctionKeyPressed += KeyboardHook_FunctionKeyPressed;
            Debug.WriteLine("ClsGameControl.InitializeCancelToken: F11取消机制已初始化");
        }

        /// <summary>
        /// KeyboardHook事件处理器，监听F11按键。
        /// </summary>
        private static void KeyboardHook_FunctionKeyPressed(object? sender, FunctionKeyEventArgs e)
        {
            if (e.Key != Key.F11)
            {
                return;
            }

            // 标记F11已处理
            e.Handled = true;

            // 触发取消操作
            TriggerCancel();
        }

        /// <summary>
        /// 手动触发取消操作，停止所有高级操作。
        /// </summary>
        public static void TriggerCancel()
        {
            if (_cancelTokenSource == null || _cancelTokenSource.IsCancellationRequested)
            {
                return;
            }

            _cancelTokenSource.Cancel();
            ClsLogger.Log("[取消] F11按下 - 所有高级操作已取消");
            Debug.WriteLine("ClsGameControl.TriggerCancel: 高级操作已被F11取消");
        }

        /// <summary>
        /// 清理CancelToken资源。
        /// </summary>
        public static void DisposeCancelToken()
        {
            if (_cancelTokenSource != null)
            {
                _cancelTokenSource.Dispose();
                _cancelTokenSource = null;
            }
        }
        #endregion

        #region 功能: 调试工具
        /// <summary>
        /// 安全的 OpenCV 图像显示方法，支持跨线程调用。
        /// 该方法自动处理线程安全性，确保 ImShow 只在适当的上下文中调用。
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="mat">要显示的图像矩阵</param>
        /// <param name="autoDestroy">是否在显示后自动销毁窗口（仅用于演示，生产环境建议 false）</param>
        // OpenCV 所有窗口操作必须在同一线程执行，使用专用线程 + 非阻塞队列
        private static readonly System.Collections.Concurrent.ConcurrentQueue<(string title, Mat mat)>
            _imShowQueue = new();

        // 用于在 OpenCV 专用线程上执行任意委托（例如 SelectROI），委托执行完毕后通过 TaskCompletionSource 返回结果
        private static readonly System.Collections.Concurrent.ConcurrentQueue<(Action action, System.Threading.ManualResetEventSlim done)>
            _cvActionQueue = new();

        private static readonly System.Threading.Thread _imShowThread = CreateImShowThread();

        private static System.Threading.Thread CreateImShowThread()
        {
            var t = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    // 执行挂起的 OpenCV 委托（如 SelectROI），这些操作必须在此线程运行
                    while (_cvActionQueue.TryDequeue(out var actionItem))
                    {
                        try
                        {
                            actionItem.action();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"CvAction 异常: {ex.Message}");
                        }
                        finally
                        {
                            actionItem.done.Set();
                        }
                    }

                    // 取出所有待显示的图像
                    while (_imShowQueue.TryDequeue(out var item))
                    {
                        try
                        {
                            Cv2.ImShow(item.title, item.mat);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"SafeImShow 异常: {ex.Message}");
                        }
                        finally
                        {
                            item.mat.Dispose();
                        }
                    }

                    // 持续泵送 OpenCV 窗口消息，保证窗口始终可响应，不卡死
                    Cv2.WaitKey(1);
                }
            });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        /// <summary>
        /// 将一个委托投递到 OpenCV 专用线程上执行并同步等待其完成。
        /// 用于 SelectROI 等必须在 OpenCV 线程上运行的阻塞操作。
        /// </summary>
        public static void RunOnCvThread(Action action)
        {
            var done = new System.Threading.ManualResetEventSlim(false);
            _cvActionQueue.Enqueue((action, done));
            done.Wait();
        }

        public static void SafeImShow(string title, Mat mat, bool autoDestroy = true)
        {
            if (mat == null || mat.Empty())
                return;

            // 克隆后投递到专用 OpenCV 线程，避免多线程窗口冲突及调用方提前释放图像
            _imShowQueue.Enqueue((title, mat.Clone()));
        }

        /// <summary>
        /// 公共的调试显示助手：根据 enabled 决定是否在图像上叠加标题并显示。
        /// 可用于替换方法内局部 DebugShow 实现以减少重复代码。
        /// </summary>
        public static void DebugShow(Mat mat, string title, bool enabled)
        {
            if (!enabled) return;
            try
            {
                using var d = mat.Clone();
                Cv2.PutText(d, title, new OpenCvSharp.Point(8, 24), HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 255, 0), 2);
                SafeImShow(title, d, autoDestroy: true);
            }
            catch { }
        }

        /// <summary>
        /// 公共的取消检查方法。若检测到 CancellationToken 被触发，则通过提供的 logAction 记录并抛出 OperationCanceledException。
        /// 如果未提供 logAction，则使用 ClsLogger.Log 进行记录。
        /// </summary>
        public static void CheckCancel(string stepName, Action<string>? logAction = null)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (logAction != null)
                        logAction($"{stepName}: 操作被F11取消");
                    else
                        ClsLogger.Log($"{stepName}: 操作被F11取消");
                }
                catch { }

                throw new OperationCanceledException("F11 Cancel requested");
            }
        }

        private static void ClickKeyAndWait(Key key, int waitMs, string methodName)
        {
            methodName = string.IsNullOrWhiteSpace(methodName) ? "Unknown" : methodName;
            string message = $"{methodName}: 按下 {GetKeyDisplayName(key)}，等待 {waitMs}ms";

            switch (methodName)
            {
                case nameof(DeleteCar):
                case nameof(UpCarPoint):
                    ClsLogger.LogPoint(message);
                    break;
                case nameof(GotoScriptRace):
                    ClsLogger.LogScript(message);
                    break;
                default:
                    ClsLogger.Log(message);
                    break;
            }
            // 在执行按键前检查是否已触发取消（使用 Point 日志用于与上层操作一致的记录）
            CheckCancel(methodName, s => ClsLogger.LogPoint(s));
            ClsLogicContorl_Ghub.ClickKey(key);
            Thread.Sleep(waitMs);
        }

        private static void ClickMouseAndWait(int button, int waitMs, string methodName)
        {
            methodName = string.IsNullOrWhiteSpace(methodName) ? "Unknown" : methodName;
            string message = $"{methodName}: 点击 {GetMouseDisplayName(button)}，等待 {waitMs}ms";

            switch (methodName)
            {
                case nameof(DeleteCar):
                case nameof(UpCarPoint):
                    ClsLogger.LogPoint(message);
                    break;
                case nameof(GotoScriptRace):
                    ClsLogger.LogScript(message);
                    break;
                default:
                    ClsLogger.Log(message);
                    break;
            }

            // 在执行鼠标点击前检查是否已触发取消
            CheckCancel(methodName, s => ClsLogger.LogPoint(s));
            ClsLogicContorl_Ghub.ClickMouse(button);
            Thread.Sleep(waitMs);
        }

        private static string GetKeyDisplayName(Key key)
        {
            return key switch
            {
                Key.Escape => "ESC",
                Key.Enter => "Enter",
                Key.PageDown => "PageDown",
                Key.PageUp => "PageUp",
                Key.Back => "Back",
                _ => key.ToString()
            };
        }

        private static string GetMouseDisplayName(int button)
        {
            return button switch
            {
                1 => "左键",
                2 => "中键",
                3 => "右键",
                4 => "侧键后",
                5 => "侧键前",
                _ => $"鼠标按钮{button}"
            };
        }
        #endregion

        #region 功能: 高级操作
        /// <summary>
        /// 进入游戏并发送回车键（高层操作）。
        /// </summary>
        public static void EnterTheGame()
        {
            RebuildCancelTokenIfNeeded();
            FocusWindowByProcessName("forzahorizon6");
            TryGetWindowRectByProcessName("forzahorizon6", out OpenCvSharp.Rect rect);
            Thread.Sleep(1000);
            ClsLogicContorl_Ghub.KeyDown(ClsLogicContorl_Ghub.ToGhubKey(Key.Enter));
            Thread.Sleep(100);
            ClsLogicContorl_Ghub.KeyUp(ClsLogicContorl_Ghub.ToGhubKey(Key.Enter));
        }

        /// <summary>
        /// 在游戏中定位并点击“选项”按钮（高层操作）。
        /// </summary>
        /// <param name="debug">如果为 true，会显示中间调试图像窗口以便开发调试。</param>
        public static void ClickOptionButton(bool debug = false)
        {
            RebuildCancelTokenIfNeeded();
            // 使用新的公共方法识别并点击"选项"
            TryRecognizeAndClickROI(ClsROI.UIElem.选项, "选项", shouldClick: true, debug: debug);
            ClsLogger.Log("已点击选项按钮");
        }

        /// <summary>
        /// 在车库中查找并进入指定的非全新车辆，然后点击“选项”按钮。
        /// 步骤1-11基本复用 UpCarPoint 的选车流程，不同点是优先识别“非全新”车辆。
        /// </summary>
        public static void DeleteCar(string manufacturerName, string modelName, string performanceScore, bool IsDebug = false)
        {
            #region 初始化与参数验证
            RebuildCancelTokenIfNeeded();
            bool finished = false;

            // 规范化字符串：去除空格、连字符、下划线并转大写，用于模糊匹配车型名称
            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
            }

            if (string.IsNullOrWhiteSpace(manufacturerName) || string.IsNullOrWhiteSpace(modelName))
            {
                ClsLogger.Log("DeleteCar: 车厂或车型为空，取消移除车辆操作。");
                return;
            }
            #endregion

            try
            {
                #region 进入车库流程
                CheckCancel("步骤1", s => ClsLogger.LogPoint(s));

                // 步骤2: 检测大世界安娜，确认当前处于大世界界面
                CheckCancel("步骤2", s => ClsLogger.LogPoint(s));
                ClsLogger.LogPoint("步骤2: 检测大世界安娜");
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.大世界安娜, "安", shouldClick: false, debug: IsDebug))
                {
                    ClsLogger.LogPoint("DeleteCar: 未识别到'安娜'，取消执行");
                    return;
                }

                // 步骤3: 按下 ESC 和 PageDown，进入车库主菜单
                CheckCancel("步骤3", s => ClsLogger.LogPoint(s));
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Escape, 1000, nameof(DeleteCar));
                ClickKeyAndWait(Key.PageDown, 500, nameof(DeleteCar));
                ClickKeyAndWait(Key.PageDown, 500, nameof(DeleteCar));
                ClickKeyAndWait(Key.Enter, 500, nameof(DeleteCar));
                ClickKeyAndWait(Key.Enter, 9000, nameof(DeleteCar));

                // 步骤8-9: 连续按两次 PageDown，导航至"我的车辆"页
                CheckCancel("步骤8", s => ClsLogger.LogPoint(s));
                ClickKeyAndWait(Key.PageDown, 500, nameof(DeleteCar));
                ClickKeyAndWait(Key.PageDown, 500, nameof(DeleteCar));

                // 步骤10: 点击"我的车辆"按钮，进入车库列表
                CheckCancel("步骤10", s => ClsLogger.LogPoint(s));
                ClsLogger.LogPoint("DeleteCar 步骤10: 检测车库界面我的车辆按钮");
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.车库界面我的车辆按钮, "我的车辆", shouldClick: true, debug: IsDebug))
                {
                    ClsLogger.LogPoint("DeleteCar: 当前界面不在住宅->车辆界面。");
                    return;
                }

                #endregion

                #region 查找并点击目标制造商
                // 步骤11: 点击"前往制造商"入口按钮
                Thread.Sleep(1000);
                ClsLogger.LogPoint($"DeleteCar 步骤11: 开始点击前往制造商 ({manufacturerName})");
                CheckCancel("步骤11", s => ClsLogger.LogPoint(s));
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.前往制造商, "前往制造商", shouldClick: true, debug: IsDebug))
                {
                    ClsLogger.LogPoint("DeleteCar: 未能找到或点击'前往制造商'");
                    return;
                }
                ClsLogger.LogPoint("DeleteCar 步骤11: 已点击前往制造商");

                // 步骤12: 最多尝试两次查找目标制造商（第一次失败则 PageUp 后重试）
                ClsLogger.LogPoint($"DeleteCar 步骤12: 开始查找并点击制造商 '{manufacturerName}'");
                CheckCancel("步骤12", s => ClsLogger.LogPoint(s));
                bool clickedManufacturer = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (TryRecognizeAndClickROI(ClsROI.UIElem.整页, manufacturerName, shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.LogPoint($"DeleteCar 步骤5-7: 已点击{manufacturerName}");
                        clickedManufacturer = true;
                        break;
                    }

                    if (attempt == 0)
                    {
                        // 第一次未找到，PageUp 后再试一次
                        ClsLogger.LogPoint($"DeleteCar 步骤13: 未找到{manufacturerName}，执行 PageUp 后重试");
                        FocusWindowByProcessName("forzahorizon6");
                        ClickKeyAndWait(Key.PageUp, 500, nameof(DeleteCar));
                    }
                }

                if (!clickedManufacturer)
                {
                    ClsLogger.LogPoint($"DeleteCar: 未找到{manufacturerName}（已重试 PageUp 一次）。");
                    return;
                }

                Thread.Sleep(800);
                #endregion

                #region 循环查找并删除目标车型
                // 可删除条件：车厂 ✓ + 车型 ✓ + 性能分 ✓ + 非全新 ✓
                ClsLogger.LogPoint($"DeleteCar: 开始扫描删除 [{manufacturerName} / {modelName} / 性能分:{performanceScore} / 非全新]");
                int deletedCount = 0;   // 累计已删除车辆数

                // 步骤1~7循环：品牌校验 -> 全页OCR -> 定位车型 -> 重设车辆框ROI -> 候选筛选 -> 点击/翻页
                while (true)
                {
                    CheckCancel("DeleteCar-扫描循环", s => ClsLogger.LogPoint(s));

                    // 1. ClsROI[车库品牌]包含品牌；不包含则结束
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.车库品牌, manufacturerName, shouldClick: false, debug: IsDebug))
                    {
                        ClsLogger.LogPoint($"DeleteCar: 车库品牌不再包含 [{manufacturerName}]，结束扫描。累计删除 {deletedCount} 辆。");
                        break;
                    }

                    // 2. OCR整个页面
                    if (!TryGetObsScreenshotMat(out Mat carFactoryMat))
                    {
                        ClsLogger.LogPoint("DeleteCar: 获取车厂界面截图失败。");
                        return;
                    }

                    using (carFactoryMat)
                    {
                        if (!TryRecognizeAndClickROI(
                            carFactoryMat,
                            new OpenCvSharp.Rect(0, 0, carFactoryMat.Width, carFactoryMat.Height),
                            out var factoryRegions,
                            searchText: null,
                            shouldClick: false,
                            debug: IsDebug,
                            debugTitle: "DeleteCar 全页OCR"))
                        {
                            ClsLogger.LogPoint("DeleteCar: 全页OCR失败。");
                            return;
                        }

                        string normalizedModel = Normalize(modelName ?? string.Empty);

                        if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车辆框, out OpenCvSharp.Rect carFrameBaseRect))
                        {
                            ClsLogger.LogPoint("DeleteCar: ROI 中未配置 '车辆框'。");
                            return;
                        }

                        var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                        var targetSize = new OpenCvSharp.Size(carFactoryMat.Width, carFactoryMat.Height);
                        OpenCvSharp.Rect carFrameScaled = ClsROI.ScaleFromBase(carFrameBaseRect, baseResolution, targetSize);

                        if (!TryCreateSafeCropRect(carFactoryMat, carFrameScaled, out OpenCvSharp.Rect safeCarFrame))
                        {
                            ClsLogger.LogPoint("DeleteCar: '车辆框' 缩放后无效。");
                            return;
                        }

                        // 3. 定位所有包含型号信息的 region
                        var modelRegions = (factoryRegions ?? new List<PaddleOcrResultRegion>())
                            .Where(p =>
                            {
                                string normalizedText = Normalize(p.Text ?? string.Empty);
                                if (string.IsNullOrEmpty(normalizedModel) || !normalizedText.Contains(normalizedModel)) return false;
                                return p.Score > 0;
                            })
                            .OrderBy(p => p.Rect.Center.X)
                            .ThenBy(p => p.Rect.Center.Y)
                            .ToList();

                        var matchedCandidates = new List<(PaddleOcrResultRegion ModelRegion, OpenCvSharp.Rect ResetRoiRect)>();

                        foreach (var modelRegion in modelRegions)
                        {
                            int modelCenterX = (int)Math.Round(modelRegion.Rect.Center.X);
                            int modelCenterY = (int)Math.Round(modelRegion.Rect.Center.Y);

                            // 4. 使用 region 顶部重设 ClsROI[车辆框] 顶部，X 中心重设为 region 的 X 中心
                            var modelBoundingRect = modelRegion.Rect.BoundingRect();
                            int roiWidth = safeCarFrame.Width;
                            int roiHeight = safeCarFrame.Height;
                            int roiX = modelCenterX - roiWidth / 2;
                            int roiY = modelBoundingRect.Top;

                            if (roiX < 0) roiX = 0;
                            if (roiY < 0) roiY = 0;
                            if (roiX + roiWidth > carFactoryMat.Width) roiX = Math.Max(0, carFactoryMat.Width - roiWidth);
                            if (roiY + roiHeight > carFactoryMat.Height) roiY = Math.Max(0, carFactoryMat.Height - roiHeight);

                            if (roiWidth <= 0 || roiHeight <= 0)
                            {
                                roiX = safeCarFrame.X;
                                roiY = safeCarFrame.Y;
                                roiWidth = safeCarFrame.Width;
                                roiHeight = safeCarFrame.Height;
                            }

                            OpenCvSharp.Rect resetRoiRect = new OpenCvSharp.Rect(roiX, roiY, roiWidth, roiHeight);
                            if (!TryCreateSafeCropRect(carFactoryMat, resetRoiRect, out OpenCvSharp.Rect safeResetRoiRect))
                            {
                                continue;
                            }

                            // 5. 使用重设ROI进行 OCR，判断是否包含“全新”且包含性能分
                            if (!TryRecognizeAndClickROI(
                                carFactoryMat,
                                safeResetRoiRect,
                                out var resetRoiRegions,
                                searchText: null,
                                shouldClick: false,
                                debug: IsDebug,
                                debugTitle: $"DeleteCar 重设ROI - {modelName}"))
                            {
                                continue;
                            }

                            bool containsBrandNew = resetRoiRegions.Any(
                                p => (p.Text ?? string.Empty).Contains("全新") && p.Score > 0);

                            bool containsPerformanceScore = true;
                            if (!string.IsNullOrWhiteSpace(performanceScore))
                            {
                                containsPerformanceScore = resetRoiRegions.Any(
                                    p => (p.Text ?? string.Empty).IndexOf(performanceScore, StringComparison.OrdinalIgnoreCase) >= 0
                                         && p.Score > 0);
                            }

                            if (!containsBrandNew && containsPerformanceScore)
                            {
                                matchedCandidates.Add((modelRegion, safeResetRoiRect));
                            }
                        }


                        // 6. 有符合则点击 X 最小、Y 最小；没符合则 Right 后回到步骤1
                        if (matchedCandidates.Count > 0)
                        {
                            var targetCandidate = matchedCandidates
                                .OrderBy(c => c.ModelRegion.Rect.Center.X)
                                .ThenBy(c => c.ModelRegion.Rect.Center.Y)
                                .First();

                            int clickX = (int)Math.Round(targetCandidate.ModelRegion.Rect.Center.X);
                            int clickY = (int)Math.Round(targetCandidate.ModelRegion.Rect.Center.Y);

                            ClsLogger.LogPoint($"DeleteCar: 找到可删除车辆 [{manufacturerName} / {modelName} / {performanceScore} / 非全新]，开始删除。");
                            CheckCancel("DeleteCar-执行删除", s => ClsLogger.LogPoint(s));
                            if (!TryClickImagePoint(carFactoryMat, clickX, clickY, "DeleteCar 点击匹配车辆"))
                                return;
                            Thread.Sleep(500);

                            if (TryRecognizeAndClickROI(ClsROI.UIElem.从车库移除车辆, "从车库移除车辆", shouldClick: true, debug: IsDebug))
                            {
                                Thread.Sleep(500);
                                ClsLogger.LogPoint("DeleteCar: 通过 OCR 点击'从车库移除车辆'。");
                                Thread.Sleep(500);
                                ClickKeyAndWait(Key.Down, 100, nameof(DeleteCar));
                                ClickKeyAndWait(Key.Enter, 1000, nameof(DeleteCar));
                            }
                            else
                            {
                                ClickKeyAndWait(Key.Enter, 500, nameof(DeleteCar));
                                for (int d = 0; d < 5; d++)
                                    ClickKeyAndWait(Key.Down, 100, nameof(DeleteCar));
                                ClickKeyAndWait(Key.Enter, 500, nameof(DeleteCar));
                                ClickKeyAndWait(Key.Down, 100, nameof(DeleteCar));
                                ClickKeyAndWait(Key.Enter, 1000, nameof(DeleteCar));
                            }

                            deletedCount++;
                            ClsLogger.LogPoint($"DeleteCar: 已删除 [{manufacturerName} / {modelName}]，累计删除 {deletedCount} 辆。");
                            Thread.Sleep(1000);
                            continue;
                        }

                        // 7. Right 次数参考已有方法（最小1次）
                        int clickRightCount = modelRegions.Count / 3 - 1 >= 1 ? modelRegions.Count / 3 - 1 : 1;
                        ClsLogger.LogPoint($"DeleteCar: 当前页无符合[全新+性能分]车辆，向右翻 {clickRightCount} 页继续扫描。");
                        FocusWindowByProcessName("forzahorizon6");
                        for (int i = 0; i < clickRightCount; i++)
                        {
                            ClickKeyAndWait(Key.Right, 500, nameof(DeleteCar));
                        }
                        Thread.Sleep(1000);
                    }
                }
                // end 扫描循环

                // 外层循环因"找不到可删除车辆"正常退出时，按 ESC 退出车库并触发完成事件
                if (!finished)
                {
                    ClickKeyAndWait(Key.Escape, 1000, nameof(DeleteCar));
                    ClickKeyAndWait(Key.Escape, 9000, nameof(DeleteCar));
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        DeleteCarCompleted?.Invoke(null, EventArgs.Empty);
                    });
                    finished = true;
                }
                #endregion
            }
            catch (OperationCanceledException)
            {
                ClsLogger.LogPoint("DeleteCar: 操作被用户取消");
                Debug.WriteLine("DeleteCar: OperationCanceledException caught - operation cancelled by F11");
            }
            catch (Exception ex)
            {
                ClsLogger.LogPoint($"DeleteCar: 发生错误 - {ex.Message}");
                Debug.WriteLine($"DeleteCar: Exception - {ex}");
            }
            finally
            {
                if (!finished)
                {
                    try
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { DeleteCarCompleted?.Invoke(null, EventArgs.Empty); });
                    }
                    catch { }
                }
            }
        }

        public static void UpCarPoint(string manufacturerName, string modelName, bool IsDebug = false)
        {
            #region 初始化
            //重设取消标志(重置热键)
            RebuildCancelTokenIfNeeded();
            //触发开始(计时)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { UpCarPointBegin?.Invoke(null, EventArgs.Empty); });
            bool finished = false;
            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var t = s.Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
                return t;
            }
            #endregion

            try
            {
                #region 预检查与进入车库
                CheckCancel("步骤1", s => ClsLogger.LogPoint(s));
                // 步骤2: 检测大世界安娜
                CheckCancel("步骤2", s => ClsLogger.LogPoint(s));
                ClsLogger.LogPoint("步骤2: 检测大世界安娜");
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.大世界安娜, "安", shouldClick: false, debug: IsDebug))
                {
                    ClsLogger.LogPoint("UpCarPoint: 未识别到'安娜'，取消执行");
                    return;
                }

                // 步骤3: 按下 ESC 和 PageDown，进入车库
                CheckCancel("步骤3", s => ClsLogger.LogPoint(s));
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                ClickKeyAndWait(Key.PageDown, 500, nameof(UpCarPoint));
                ClickKeyAndWait(Key.PageDown, 500, nameof(UpCarPoint));
                ClickKeyAndWait(Key.Enter, 500, nameof(UpCarPoint));
                ClickKeyAndWait(Key.Enter, 9000, nameof(UpCarPoint));

                // 步骤8: 按 PageDown 两次，进入我的车辆页面
                CheckCancel("步骤8", s => ClsLogger.LogPoint(s));
                ClickKeyAndWait(Key.PageDown, 500, nameof(UpCarPoint));
                ClickKeyAndWait(Key.PageDown, 1000, nameof(UpCarPoint));
                #endregion

                #region 主循环: 查找车辆并升级
                // 主循环：当点数 >= 30 时循环升级同一辆车
                while (true)
                {
                    CheckCancel("循环开始", s => ClsLogger.LogPoint(s));
                    #region 我的车辆与车库检测
                    // 步骤1: 检测车库界面我的车辆按钮
                    CheckCancel("步骤10", s => ClsLogger.LogPoint(s));
                    ClsLogger.LogPoint("步骤10: 检测车库界面我的车辆按钮");

                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.车库界面我的车辆按钮, "我的车辆", shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 当前界面不在住宅->车辆界面。");
                        return;
                    }
                    #endregion

                    #region 前往制造商并选择品牌
                    Thread.Sleep(1000);
                    ClsLogger.LogPoint($"步骤11: 开始点击前往制造商 ({manufacturerName})");
                    // 步骤11: 点击前往制造商前检查取消
                    CheckCancel("步骤11", s => ClsLogger.LogPoint(s));

                    // 使用新的公共方法识别并点击"前往制造商"
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.前往制造商, "前往制造商", shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 未能找到或点击'前往制造商'");
                        return;
                    }

                    ClsLogger.LogPoint("步骤11: 已点击前往制造商");

                    // 5~7: 点击制造商（尝试使用通用 ROI OCR 方法以减少重复代码）
                    ClsLogger.LogPoint($"步骤12: 开始查找并点击制造商 '{manufacturerName}'");
                    // 步骤12: 查找并点击制造商前检查取消
                    CheckCancel("步骤12", s => ClsLogger.LogPoint(s));
                    bool clickedSubaru = false;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        // 优先使用 TryRecognizeAndClickROI 在预定义的品牌 ROI 中搜索并点击
                        if (TryRecognizeAndClickROI(ClsROI.UIElem.整页, manufacturerName, shouldClick: true, debug: IsDebug))
                        {
                            ClsLogger.LogPoint($"步骤5-7: 已点击{manufacturerName}");
                            clickedSubaru = true;
                            break;
                        }

                        if (attempt == 0)
                        {
                            ClsLogger.LogPoint($"步骤13: 未找到{manufacturerName}，执行 PageUp 后重试");
                            FocusWindowByProcessName("forzahorizon6");
                            ClickKeyAndWait(Key.PageUp, 500, nameof(UpCarPoint));
                        }
                    }

                    if (!clickedSubaru)
                    {
                        ClsLogger.LogPoint($"UpCarPoint: 未找到{manufacturerName}（已重试 PageUp 一次）。");
                        return;
                    }

                    Thread.Sleep(800);
                    #endregion

                    #region 查找车型并检测“全新”
                    // 8~11: 查找车型，扩展 ROI，检测“全新”；若未找到则切换并检查品牌后重试
                    ClsLogger.LogPoint($"步骤14: 开始查找车型 '{modelName}' 与全新标记");
                    bool clickedBrandNew = false;
                    int OldTypeCarNum = 0;//非全新但匹配车型数量（用于多重翻页）

                    for (int round = 0; round < 20 && !clickedBrandNew; round++)
                    {
                        // 步骤14: 查找车型与'全新'前检查取消
                        CheckCancel("步骤14", s => ClsLogger.LogPoint(s));
                        if (!TryGetObsScreenshotMat(out Mat carFactoryMat))
                        {
                            ClsLogger.LogPoint("UpCarPoint: 获取车厂界面截图失败。");
                            return;
                        }

                        using (carFactoryMat)
                        {
                            DebugShow(carFactoryMat, $"Step7 Original - Factory (round {round + 1})", IsDebug);
                            if (!TryRecognizeAndClickROI(
                                carFactoryMat,
                                new OpenCvSharp.Rect(0, 0, carFactoryMat.Width, carFactoryMat.Height),
                                out var factoryRegions,
                                searchText: null,
                                shouldClick: false,
                                debug: IsDebug,
                                debugTitle: $"UpCarPoint 工厂页OCR (round {round + 1})"))
                            {
                                ClsLogger.LogPoint("UpCarPoint: 车厂界面OCR识别失败。");
                                return;
                            }

                            var normalizedModel = Normalize(modelName ?? string.Empty);

                            OpenCvSharp.Rect excludeScaledRect = new OpenCvSharp.Rect();
                            if (ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车库中当前车型, out OpenCvSharp.Rect excludeBaseRect))
                            {
                                excludeScaledRect = ClsROI.ScaleFromBase(excludeBaseRect, new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight), new OpenCvSharp.Size(carFactoryMat.Width, carFactoryMat.Height));
                            }

                            if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车辆框, out OpenCvSharp.Rect carFrameBaseRect))
                            {
                                ClsLogger.LogPoint("UpCarPoint: ROI 中未配置 '车辆框'。");
                                return;
                            }

                            var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                            var targetSize = new OpenCvSharp.Size(carFactoryMat.Width, carFactoryMat.Height);
                            OpenCvSharp.Rect carFrameScaled = ClsROI.ScaleFromBase(carFrameBaseRect, baseResolution, targetSize);

                            if (!TryCreateSafeCropRect(carFactoryMat, carFrameScaled, out OpenCvSharp.Rect safeCarFrame))
                            {
                                ClsLogger.LogPoint("UpCarPoint: '车辆框' 缩放后无效。");
                                return;
                            }

                            var modelRegions = (factoryRegions ?? new List<PaddleOcrResultRegion>())
                                .Where(p =>
                                {
                                    var t = Normalize(p.Text ?? string.Empty);
                                    if (string.IsNullOrEmpty(normalizedModel) || !t.Contains(normalizedModel)) return false;
                                    if (p.Score <= 0) return false;

                                    if (excludeScaledRect.Width > 0 && excludeScaledRect.Height > 0)
                                    {
                                        var cx = (int)Math.Round(p.Rect.Center.X);
                                        var cy = (int)Math.Round(p.Rect.Center.Y);
                                        if (cx >= excludeScaledRect.X && cy >= excludeScaledRect.Y && cx < excludeScaledRect.X + excludeScaledRect.Width && cy < excludeScaledRect.Y + excludeScaledRect.Height)
                                            return false;
                                    }

                                    return true;
                                })
                                .OrderBy(p => p.Rect.Center.Y)
                                .ThenBy(p => p.Rect.Center.X)
                                .ToList();

                            if (modelRegions.Count == 0)
                            {
                                ClsLogger.LogPoint($"步骤15: 未找到车型 '{modelName}'（第{round + 1}轮）。");
                            }
                            else
                            {
                                OldTypeCarNum = modelRegions.Count;
                            }

                            for (int i = 0; i < modelRegions.Count; i++)
                            {
                                var modelRegion = modelRegions[i];
                                RotatedRect modelRect = modelRegion.Rect;
                                int modelCenterX = (int)Math.Round(modelRect.Center.X);
                                int modelCenterY = (int)Math.Round(modelRect.Center.Y);

                                int expW = safeCarFrame.Width;
                                int expH = safeCarFrame.Height;

                                int expX = modelCenterX - expW / 2;
                                int marginFromTop = (int)Math.Round(expH * 0.06);
                                int expY = modelCenterY - marginFromTop;

                                if (expX < 0) expX = 0;
                                if (expY < 0) expY = 0;
                                if (expX + expW > carFactoryMat.Width) expX = Math.Max(0, carFactoryMat.Width - expW);
                                if (expY + expH > carFactoryMat.Height) expY = Math.Max(0, carFactoryMat.Height - expH);

                                if (expW <= 0 || expH <= 0)
                                {
                                    expX = safeCarFrame.X;
                                    expY = safeCarFrame.Y;
                                    expW = safeCarFrame.Width;
                                    expH = safeCarFrame.Height;
                                }

                                OpenCvSharp.Rect expandedRect = new OpenCvSharp.Rect(expX, expY, expW, expH);

                                if (IsDebug)
                                {
                                    try
                                    {
                                        using var mark = carFactoryMat.Clone();
                                        var mc = new OpenCvSharp.Point(modelCenterX, modelCenterY);
                                        Cv2.Circle(mark, mc, 8, new Scalar(0, 0, 255), 3);
                                        Cv2.Rectangle(mark, new OpenCvSharp.Point(expandedRect.X, expandedRect.Y), new OpenCvSharp.Point(expandedRect.X + expandedRect.Width, expandedRect.Y + expandedRect.Height), new Scalar(255, 0, 0), 2);
                                        Cv2.PutText(mark, $"ModelCandidate {i + 1}", new OpenCvSharp.Point(mc.X + 10, mc.Y), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
                                        SafeImShow($"Step7 Marked - Model and Expanded ({i + 1})", mark, autoDestroy: true);
                                    }
                                    catch { }
                                }

                                if (!TryCreateSafeCropRect(carFactoryMat, expandedRect, out OpenCvSharp.Rect safeExpandedRect))
                                {
                                    ClsLogger.LogPoint($"步骤9: 第{i + 1}个车型候选扩展 ROI 无效，跳过。");
                                    continue;
                                }

                                using Mat expandedCropped = new Mat(carFactoryMat, safeExpandedRect);
                                DebugShow(expandedCropped, $"Step8 Expanded ROI Crop ({i + 1})", IsDebug);
                                if (!TryRecognizeAndClickROI(
                                    carFactoryMat,
                                    safeExpandedRect,
                                    out var expandedRegions,
                                    searchText: "全新",
                                    shouldClick: true,
                                    debug: IsDebug,
                                    debugTitle: $"Step8 Expanded ROI Crop ({i + 1})"))
                                {
                                    ClsLogger.LogPoint($"步骤17: 第{i + 1}个匹配车型扩展 ROI 内未检测到 '全新'。");
                                    continue;
                                }

                                ClsLogger.LogPoint($"步骤18: 第{i + 1}个匹配车型扩展 ROI 内检测到 '全新'。");
                                DebugShow(expandedCropped, $"Step10 Show - {modelName} Contains BrandNew ({i + 1})", IsDebug);

                                // 步骤19: 点击'全新'之前检查取消
                                CheckCancel("步骤19", s => ClsLogger.LogPoint(s));
                                Thread.Sleep(500);
                                ClickKeyAndWait(Key.Enter, 500, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Up, 100, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Up, 100, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Up, 100, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Up, 100, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Up, 100, nameof(UpCarPoint));
                                ClickKeyAndWait(Key.Enter, 0, nameof(UpCarPoint));
                                ClsLogger.LogPoint("步骤19: 已点击全新并按下回车，等待12秒");
                                Thread.Sleep(12000);
                                ClickKeyAndWait(Key.Escape, 0, nameof(UpCarPoint));
                                ClsLogger.LogPoint("步骤11: 已按下 ESC");

                                clickedBrandNew = true;
                                break;
                            }
                        }

                        if (clickedBrandNew)
                        {
                            break;
                        }

                        ClsLogger.LogPoint("步骤20: 当前页面所有匹配车型均未检测到'全新'，按下→并检查车库品牌是否仍为 SUBARU。");
                        FocusWindowByProcessName("forzahorizon6");

                        //根据非全新但匹配车型数量翻页，避免翻页过少
                        int ClickKeyCount = OldTypeCarNum / 3 - 1 >= 1 ? OldTypeCarNum / 3 - 1 : 1;//最小为1
                        for (int i = 0; i < ClickKeyCount; i++)
                        {
                            ClickKeyAndWait(Key.Right, 500, nameof(UpCarPoint));
                        }


                        if (!TryRecognizeAndClickROI(ClsROI.UIElem.车库品牌, manufacturerName, shouldClick: false, debug: IsDebug))
                        {
                            // 品牌不再是 SUBARU，说明已没有更多全新车型，流程结束
                            ClsLogger.LogPoint("步骤21: 车库品牌不再包含 SUBARU，已无更多全新车型，流程完成。");
                            ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                            ClickKeyAndWait(Key.Escape, 10000, nameof(UpCarPoint));
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { AllCarPointComplete?.Invoke(null, EventArgs.Empty); });
                            return;
                        }

                        // 品牌仍是 SUBARU，进入第N轮完整检测（车型 + 全新）
                        ClsLogger.LogPoint($"步骤22: 车库品牌仍为 SUBARU，进入第{round + 1}轮检测车型与'全新'。");
                    }

                    if (!clickedBrandNew)
                    {
                        ClsLogger.LogPoint("步骤23: 再次检测后仍未找到'全新'，流程完成。");
                        return;
                    }
                    #endregion


                    Thread.Sleep(1000);

                    #region 升级与熟练度界面交互
                    // 24: 使用 TryRecognizeAndClickROI 点击升级与调教
                    CheckCancel("步骤24", s => ClsLogger.LogPoint(s));
                    ClsLogger.LogPoint("步骤24: 点击 ROI-升级与调教");
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.升级与调教, searchText: null, shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 未能点击 '升级与调教'。");
                        return;
                    }

                    Thread.Sleep(500);

                    // 25: 使用 TryRecognizeAndClickROI 点击车辆熟练度
                    CheckCancel("步骤25", s => ClsLogger.LogPoint(s));
                    ClsLogger.LogPoint("步骤25: 点击 ROI-车辆熟练度");
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.车辆熟练度, searchText: null, shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 未能点击 '车辆熟练度'。");
                        return;
                    }

                    Thread.Sleep(800);
                    #endregion

                    #region 识别点数并决定是否执行加点
                    // 26: 采图，使用ROI：熟练度点数，裁剪后判断点数是否 >=30
                    // 步骤26: 识别熟练度点数前检查取消
                    CheckCancel("步骤26", s => ClsLogger.LogPoint(s));
                    ClsLogger.LogPoint("步骤26: 识别熟练度点数");
                    int currentPoint = 0;
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.加点界面熟练度点数, out var pointRegions, searchText: null, shouldClick: false, debug: IsDebug))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 步骤14识别熟练度点数失败。");
                        return;
                    }

                    var values = new List<int>();
                    foreach (var r in pointRegions)
                    {
                        var ms = System.Text.RegularExpressions.Regex.Matches(r.Text ?? string.Empty, @"\d+");
                        foreach (System.Text.RegularExpressions.Match m in ms)
                        {
                            if (int.TryParse(m.Value, out int v)) values.Add(v);
                        }
                    }

                    currentPoint = values.Count > 0 ? values.Max() : 0;
                    ClsLogger.LogPoint($"步骤14: 当前熟练度点数={currentPoint}");
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        DetectPoint?.Invoke(null, currentPoint);
                    });

                    if (currentPoint < 30)
                    {
                        ClsLogger.LogPoint("步骤26: 点数小于30，终止循环。");
                        ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                        ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                        Thread.Sleep(1000);
                        // 循环结束
                        break;
                    }
                    #endregion

                    #region 加点按键序列与循环判断
                    // 27: 依次按键序列
                    CheckCancel("步骤27", s => ClsLogger.LogPoint(s));
                    ClsLogger.LogPoint("步骤27: 开始执行按键序列");
                    FocusWindowByProcessName("forzahorizon6");
                    //首个技能点
                    ClickKeyAndWait(Key.Enter, 1500, nameof(UpCarPoint));
                    //后续技能点
                    Key[] directions = new[] { Key.Right, Key.Up, Key.Up, Key.Up, Key.Left };
                    foreach (var dir in directions)
                    {
                        ClickKeyAndWait(dir, 300, nameof(UpCarPoint));
                        ClickKeyAndWait(Key.Enter, 1000, nameof(UpCarPoint));
                    }

                    ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                    ClickKeyAndWait(Key.Escape, 2000, nameof(UpCarPoint));
                    ClsLogger.LogPoint("步骤27: 按键序列执行完成");
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        SingelCarPointComplete?.Invoke(null, EventArgs.Empty);
                    });

                    // 根据检测到的点数判断执行完按键序列后是否还应继续。
                    // 如果当前点数减去 30 后仍然 >= 30，则继续；否则不重复执行（避免剩余点数不足以再次升级）。
                    int remainingAfter = currentPoint - 30;
                    if (remainingAfter >= 30)
                    {
                        ClsLogger.LogPoint($"循环继续: 点数 {currentPoint} >= 30，扣除30后剩余 {remainingAfter} >= 30，继续升级同一辆车");
                    }
                    else
                    {
                        ClsLogger.LogPoint($"循环结束: 点数 {currentPoint} - 30 = {remainingAfter} 小于30，执行完加点后不再重复。");
                        //退回主界面
                        ClickKeyAndWait(Key.Escape, 1000, nameof(UpCarPoint));
                        ClickKeyAndWait(Key.Escape, 7000, nameof(UpCarPoint));
                        finished = true;
                        break;
                    }
                    #endregion
                } // 结束 while 循环
                #endregion
            }
            catch (OperationCanceledException)
            {
                ClsLogger.LogPoint("UpCarPoint: 操作被用户取消");
                Debug.WriteLine("UpCarPoint: OperationCanceledException caught - operation cancelled by F11");
            }
            catch (Exception ex)
            {
                ClsLogger.LogPoint($"UpCarPoint: 发生错误 - {ex.Message}");
                Debug.WriteLine($"UpCarPoint: Exception - {ex}");
            }
            finally
            {
                #region 完成和清理
                if (!finished)
                {
                    try { System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { AllCarPointComplete?.Invoke(null, EventArgs.Empty); }); } catch { }
                }
                #endregion
            }
        }

        public static void BuyCar(int quantity, string manufacturerName, string modelName, bool IsDebug = false)
        {
            RebuildCancelTokenIfNeeded();

            bool finished = false;

            // 使用公共 DebugShow(mat, title, enabled) 与 CheckCancel(stepName, logAction) 方法

            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
            }

            if (quantity <= 0)
            {
                ClsLogger.Log($"BuyCar: 购买数量无效 - {quantity}");
                return;
            }

            if (string.IsNullOrWhiteSpace(manufacturerName) || string.IsNullOrWhiteSpace(modelName))
            {
                ClsLogger.Log("BuyCar: 车厂或车型为空，取消购买操作。");
                return;
            }

            try
            {
                CheckCancel("步骤1");
                ClsLogger.Log($"BuyCar: 开始购买 {manufacturerName} {modelName} x{quantity}");

                // 步骤2: 检测大世界安娜
                CheckCancel("步骤2");
                ClsLogger.Log("步骤2: 检测大世界安娜");
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.大世界安娜, "安", shouldClick: false, debug: IsDebug))
                {
                    ClsLogger.Log("BuyCar: 未识别到'安娜'，取消执行");
                    return;
                }

                // 步骤3: 按下 ESC 和 PageDown，进入车库
                CheckCancel("步骤3");
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Escape, 1000, nameof(BuyCar));
                ClickKeyAndWait(Key.PageDown, 500, nameof(BuyCar));
                ClickKeyAndWait(Key.PageDown, 500, nameof(BuyCar));
                ClickKeyAndWait(Key.Enter, 500, nameof(BuyCar));
                ClickKeyAndWait(Key.Enter, 10000, nameof(BuyCar));


                // 步骤8. 按 PageDown 两次，进入我的车辆页面
                CheckCancel("步骤8");

                // 步骤10: 使用 ClsROI【收集簿】取图并判断，找到后点击，等待300ms
                if (!TryRecognizeAndClickROI(ClsROI.UIElem.收集簿, "收集", shouldClick: true, debug: IsDebug))
                {
                    ClsLogger.Log("BuyCar: 当前界面未识别到 '收集簿'，取消购买操作。");
                    return;
                }
                Thread.Sleep(300);

                CheckCancel("步骤11");

                // 步骤12: 点击右、等待200ms回车、等待500ms、下、等待200ms、回车
                CheckCancel("步骤12");
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Right, 200, nameof(BuyCar));
                ClickKeyAndWait(Key.Enter, 500, nameof(BuyCar));
                ClickKeyAndWait(Key.Down, 200, nameof(BuyCar));
                ClickKeyAndWait(Key.Enter, 500, nameof(BuyCar));

                // 步骤13: 按下 backspace 进入车厂界面
                CheckCancel("步骤13");
                ClickKeyAndWait(Key.Back, 500, nameof(BuyCar));

                // 步骤14-15: 查找车厂并点击，没找到则 PageUp 后再查找一次
                CheckCancel("步骤14");
                bool clickedManufacturer = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (TryRecognizeAndClickROI(ClsROI.UIElem.整页, manufacturerName, shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.Log($"BuyCar: 已点击车厂 {manufacturerName}");
                        clickedManufacturer = true;
                        break;
                    }

                    if (attempt == 0)
                    {
                        FocusWindowByProcessName("forzahorizon6");
                        ClickKeyAndWait(Key.PageUp, 1000, nameof(BuyCar));
                    }
                }

                if (!clickedManufacturer)
                {
                    ClsLogger.Log($"BuyCar: 未找到车厂 {manufacturerName}");
                    return;
                }

                Thread.Sleep(500);

                // 步骤16: 进入对应车厂界面后查找对应车型（未查找到则按下↓，最多尝试5次）
                CheckCancel("步骤16");
                bool clickedModel = false;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (TryRecognizeAndClickROI(ClsROI.UIElem.整页, modelName, shouldClick: true, debug: IsDebug))
                    {
                        ClsLogger.Log($"BuyCar: 已点击车型 {modelName}");
                        clickedModel = true;
                        break;
                    }

                    if (attempt < 4)
                    {
                        FocusWindowByProcessName("forzahorizon6");
                        ClickKeyAndWait(Key.Down, 300, nameof(BuyCar));
                    }
                }

                if (!clickedModel)
                {
                    ClsLogger.Log($"BuyCar: 未找到车型 {modelName}");
                    return;
                }

                // 步骤17: 根据传入数量从空格开始循环购买
                Thread.Sleep(300);
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Enter, 300, nameof(BuyCar));

                for (int i = 0; i < quantity; i++)
                {
                    CheckCancel($"步骤17-购买第{i + 1}次");
                    ClsLogger.Log($"BuyCar: 执行第 {i + 1}/{quantity} 次购买");

                    ClickKeyAndWait(Key.Space, 400, nameof(BuyCar));
                    ClickKeyAndWait(Key.Down, 400, nameof(BuyCar));
                    ClickKeyAndWait(Key.Enter, 400, nameof(BuyCar));
                    ClickKeyAndWait(Key.Enter, 400, nameof(BuyCar));
                    ClickKeyAndWait(Key.Enter, 1000, nameof(BuyCar));
                    ClickKeyAndWait(Key.Enter, 1500, nameof(BuyCar));
                }

                // 步骤18: 购买完成之后点击5次ESC，每个间隔1秒，然后等待5秒发送购买完成事件
                CheckCancel("步骤18");
                for (int i = 0; i < 5; i++)
                {
                    ClickKeyAndWait(Key.Escape, 1000, nameof(BuyCar));
                }

                Thread.Sleep(7000);
                ClsLogger.Log("BuyCar: 购买完成事件已触发");
                finished = true;
            }
            catch (OperationCanceledException)
            {
                ClsLogger.Log("BuyCar: 操作被用户取消");
                Debug.WriteLine("BuyCar: OperationCanceledException caught - operation cancelled by F11");
            }
            catch (Exception ex)
            {
                ClsLogger.Log($"BuyCar: 发生错误 - {ex.Message}");
                Debug.WriteLine($"BuyCar: Exception - {ex}");
            }
            finally
            {
                if (!finished)
                {
                    try { System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { BuyCarCompleted?.Invoke(null, EventArgs.Empty); }); } catch { }
                }
            }
        }


        /// <summary>
        /// 通用区域文本识别方法：获取 OBS 截图，从指定 ROI 识别文本，可选点击元素。
        /// 该方法封装了常用的 OCR 流程，减少代码重复。
        /// </summary>
        /// <param name="roiElement">ClsROI.UIElem 中定义的 ROI 元素</param>
        /// <param name="searchText">要搜索的文本（如果为 null 或空，仅返回识别的所有文本）</param>
        /// <param name="shouldClick">是否在识别到目标文本后点击该位置</param>
        /// <param name="debug">是否显示调试窗口</param>
        public static bool TryRecognizeAndClickROI(
        /// <returns>如果找到搜索文本返回 true，否则返回 false；如果未设置搜索文本，则返回识别是否成功</returns>
            ClsROI.UIElem roiElement,
            string? searchText = null,
            bool shouldClick = false,
            bool debug = false)
        {
            return TryRecognizeAndClickROI(roiElement, out _, searchText, shouldClick, debug);
        }

        public static bool TryRecognizeAndClickROI(
            ClsROI.UIElem roiElement,
            out List<PaddleOcrResultRegion> regions,
            string? searchText = null,
            bool shouldClick = false,
            bool debug = false)
        {
            regions = new List<PaddleOcrResultRegion>();
            try
            {
                if (!TryGetObsScreenshotMat(out Mat imageMat))
                {
                    ClsLogger.LogGlobal($"TryRecognizeAndClickROI: 未能获取 OBS 截图");
                    return false;
                }

                using (imageMat)
                {
                    if (!ClsROI.TargetRects.TryGetValue(roiElement, out OpenCvSharp.Rect baseRect))
                    {
                        ClsLogger.LogGlobal($"TryRecognizeAndClickROI: ROI 中未配置 '{roiElement}'");
                        return false;
                    }

                    var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                    var targetSize = new OpenCvSharp.Size(imageMat.Width, imageMat.Height);
                    OpenCvSharp.Rect scaledRect = ClsROI.ScaleFromBase(baseRect, baseResolution, targetSize);

                    if (!TryCreateSafeCropRect(imageMat, scaledRect, out OpenCvSharp.Rect cropRect))
                    {
                        ClsLogger.LogGlobal($"TryRecognizeAndClickROI: ROI 裁剪无效 - {roiElement}");
                        return false;
                    }

                    using Mat cropped = new Mat(imageMat, cropRect);

                    if (debug)
                    {
                        try { SafeImShow($"TryRecognizeAndClickROI - {roiElement}", cropped, autoDestroy: true); }
                        catch (Exception ex)
                        {
                            ClsLogger.LogGlobal(ex.Message);
                        }
                    }

                    if (!TryEncodeMatAsPng(cropped, out byte[] croppedBytes))
                    {
                        ClsLogger.LogGlobal($"TryRecognizeAndClickROI: 图像编码失败 - {roiElement}");
                        return false;
                    }

                    var ocrRst = ClsOCR.RecognizeFromBytes(croppedBytes);
                    if (ocrRst?.Regions == null || ocrRst.Regions.Length == 0)
                    {
                        ClsLogger.LogGlobal($"TryRecognizeAndClickROI: OCR 未识别到任何文本 - {roiElement}");
                        return false;
                    }

                    regions = ocrRst.Regions.ToList();

                    bool found = false;
                    double? targetCenterX = null;
                    double? targetCenterY = null;

                    foreach (var region in ocrRst.Regions)
                    {
                        string regionText = region.Text ?? "";

                        if (string.IsNullOrEmpty(searchText))
                        {
                            found = true;
                            targetCenterX = region.Rect.Center.X + cropRect.X;
                            targetCenterY = region.Rect.Center.Y + cropRect.Y;
                            break;
                        }

                        if (regionText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            targetCenterX = region.Rect.Center.X + cropRect.X;
                            targetCenterY = region.Rect.Center.Y + cropRect.Y;
                            break;
                        }
                    }

                    if (!found)
                    {
                        ClsLogger.LogGlobal($"TryRecognizeAndClickROI: 未找到文本 '{searchText}' - {roiElement}");
                        return false;
                    }

                    if (shouldClick && targetCenterX.HasValue && targetCenterY.HasValue)
                    {
                        Thread.Sleep(200);
                        if (!TryClickImagePoint(imageMat, targetCenterX.Value, targetCenterY.Value,
                            $"TryRecognizeAndClickROI 点击 {roiElement} - '{searchText}'"))
                        {
                            ClsLogger.LogGlobal($"TryRecognizeAndClickROI: 点击失败 - {roiElement}");
                            return false;
                        }
                        Thread.Sleep(300);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryRecognizeAndClickROI 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通用区域文本识别方法（扩展版）：获取识别到的所有区域信息。
        /// 返回识别到的所有区域列表，调用方可根据需要处理多个识别结果。
        /// </summary>
        /// <param name="roiElement">ClsROI.UIElem 中定义的 ROI 元素</param>
        /// <param name="regions">输出：识别到的所有区域列表</param>
        /// <param name="debug">是否显示调试窗口</param>
        /// <returns>如果识别成功返回 true，否则返回 false</returns>
        public static bool TryRecognizeROIRegions(
            ClsROI.UIElem roiElement,
            out List<PaddleOcrResultRegion> regions,
            bool debug = false)
        {
            return TryRecognizeAndClickROI(roiElement, out regions, searchText: null, shouldClick: false, debug: debug);
        }

        private static bool TryRecognizeAndClickROI(
            Mat imageMat,
            OpenCvSharp.Rect roiRect,
            out List<PaddleOcrResultRegion> regions,
            string? searchText = null,
            bool shouldClick = false,
            bool debug = false,
            string? debugTitle = null)
        {
            regions = new List<PaddleOcrResultRegion>();
            try
            {
                if (imageMat == null || imageMat.Empty())
                {
                    return false;
                }

                if (!TryCreateSafeCropRect(imageMat, roiRect, out OpenCvSharp.Rect cropRect))
                {
                    return false;
                }

                using Mat cropped = new Mat(imageMat, cropRect);
                if (debug)
                {
                    try { SafeImShow(debugTitle ?? "TryRecognizeAndClickROI-Rect", cropped, autoDestroy: true); } catch { }
                }

                if (!TryEncodeMatAsPng(cropped, out byte[] croppedBytes))
                {
                    return false;
                }

                var ocrRst = ClsOCR.RecognizeFromBytes(croppedBytes);
                if (ocrRst?.Regions == null || ocrRst.Regions.Length == 0)
                {
                    return false;
                }

                regions = ocrRst.Regions.ToList();

                bool found = false;
                double? targetCenterX = null;
                double? targetCenterY = null;
                foreach (var region in ocrRst.Regions)
                {
                    string regionText = region.Text ?? string.Empty;
                    if (string.IsNullOrEmpty(searchText) || regionText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        targetCenterX = region.Rect.Center.X + cropRect.X;
                        targetCenterY = region.Rect.Center.Y + cropRect.Y;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }

                if (shouldClick && targetCenterX.HasValue && targetCenterY.HasValue)
                {
                    return TryClickImagePoint(imageMat, targetCenterX.Value, targetCenterY.Value, "TryRecognizeAndClickROI-Rect 点击");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // SelectAndClickHighestPerformanceRacecar 已内联至 GotoScriptRace，方法已移除

        /// <summary>
        /// 执行脚本赛车流程：识别大世界安娜，提取技能点，输入蓝图代码，并循环执行赛车任务。
        /// 支持 F12 取消、事件通知（执行开始、进度变更、完成）
        /// </summary>
        /// <param name="blueprintCode">蓝图代码字符串（使用键盘输入）</param>
        /// <param name="manufacturerName">车厂名称（例如: 斯巴鲁）</param>
        /// <param name="modelName">车型名称（例如: 22B）</param>
        /// <param name="debug">是否显示调试窗口</param>
        public static void GotoScriptRace(string blueprintCode, string manufacturerName, string modelName, int pointsPerRace = 9, bool debug = false)
        {
            RebuildCancelTokenIfNeeded();
            bool finished = false;
            const int restartDetectTimeoutMs = 120000; // 2分钟超时
            const int restartDetectIntervalMs = 500; // 每500ms检测一次

            // 使用公共 DebugShow(mat, title, enabled) 与 CheckCancel(stepName, logAction)

            try
            {

                CheckCancel("步骤1");
                ClsLogger.LogScript("=== GotoScriptRace 开始执行 ===");

                // 触发蓝图执行开始事件（直接在流程中触发）
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { BlueprintExecutionStarted?.Invoke(null, EventArgs.Empty); });

                // 步骤1-2: 识别大世界安娜区域并验证
                CheckCancel("步骤2");
                ClsLogger.LogScript("步骤2: 识别大世界安娜区域");


                if (!TryRecognizeAndClickROI(ClsROI.UIElem.大世界安娜, "安", shouldClick: false, debug: debug))
                {
                    ClsLogger.LogScript("步骤3: 未识别到'安娜'，取消执行");
                    return;
                }

                // 步骤2: 按下 ESC 和 PageDown
                CheckCancel("步骤4");
                ClsLogger.LogScript("步骤4: 按下 ESC，等待500ms");
                FocusWindowByProcessName("forzahorizon6");
                ClickKeyAndWait(Key.Escape, 1000, nameof(GotoScriptRace));
                ClsLogger.LogScript("步骤5: 按下 PageDown，等待300ms");
                ClickKeyAndWait(Key.PageDown, 500, nameof(GotoScriptRace));

                // 步骤3: 识别车辆界面技术点数
                CheckCancel("步骤6");
                ClsLogger.LogScript("步骤6: 识别车辆界面技术点数");

                int techPoints = 0;
                if (TryRecognizeROIRegions(ClsROI.UIElem.技术点数可用, out var regions, debug: debug))
                {
                    foreach (var region in regions)
                    {
                        string text = region.Text ?? "";
                        // 移除 "技术点" 或 "可用" 等文字，提取数字部分
                        text = text.Replace("技术点数可用", "").Trim();
                        if (int.TryParse(text, out int points))
                        {
                            techPoints = points;
                            ClsLogger.LogScript($"步骤7: 识别到技术点数: {techPoints}");
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { DetectPoint?.Invoke(null, techPoints); });
                            break;
                        }
                    }
                }

                if (techPoints <= 0)
                {
                    ClsLogger.LogScript("步骤8: 未能识别到有效的技术点数");
                    return;
                }

                // 根据单局点数计算需要循环的次数
                int pointsNeeded = 999 - techPoints;
                int loopsRequired = (pointsNeeded + pointsPerRace - 1) / pointsPerRace; // 向上取整
                ClsLogger.LogScript($"步骤8.5: 当前技术点数={techPoints}，需要获得{pointsNeeded}点，单局{pointsPerRace}点，需要循环{loopsRequired}次");
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { DetectPoint?.Invoke(null, techPoints); });

                // 步骤4: 按 3 次 PageDown，然后按 Enter/Enter/BackSpace/UP/Enter
                CheckCancel("步骤9");
                //移走鼠标
                ClsLogicContorl_Ghub.Move(-4096, -4096);
                ClsLogger.LogScript("步骤9: 按 3 次 PageDown");
                for (int i = 0; i < 3; i++)
                {
                    ClickKeyAndWait(Key.PageDown, 200, nameof(GotoScriptRace));
                }
                Thread.Sleep(500);
                ClsLogger.LogScript("步骤10: 按 Enter/Enter/BackSpace/UP/Enter");
                ClickKeyAndWait(Key.Enter, 1000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 1000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Back, 1000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Up, 500, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 1000, nameof(GotoScriptRace));
                // 步骤5: 使用键盘输入蓝图代码
                CheckCancel("步骤11");
                ClsLogger.LogScript($"步骤11: 输入蓝图代码 (长度: {blueprintCode?.Length ?? 0})");
                if (!string.IsNullOrEmpty(blueprintCode))
                {
                    ClsLogicContorl_Ghub.InputText(blueprintCode);
                    Thread.Sleep(1000);
                }
                // 步骤6: 按 Enter/Down/Enter，等待 5 秒
                CheckCancel("步骤12");
                ClsLogger.LogScript("步骤12: 按 Enter/Down/Enter");
                ClickKeyAndWait(Key.Enter, 500, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Down, 200, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 4000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 1000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 0, nameof(GotoScriptRace));
                ClsLogger.LogScript("步骤13: 等待 5 秒");
                Thread.Sleep(3000);//进入蓝图

                // 步骤13.5: 在点击前往制造商之前，先检查车辆卡片界面当前驾驶的车辆范围内是否已包含车型和字母R
                CheckCancel("步骤13.5");
                ClsLogger.LogScript("步骤13.5: 检查车辆卡片界面当前驾驶的车辆范围内是否包含车型名称和字母R");
                bool skipManufacturerSelection = false;

                string Normalize7(string s)
                {
                    if (string.IsNullOrEmpty(s)) return string.Empty;
                    return s.Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
                }

                if (TryRecognizeROIRegions(ClsROI.UIElem.车辆卡片界面当前驾驶的车辆, out var cardRegions, debug: debug))
                {
                    string normalizedCardModel = Normalize7(modelName ?? string.Empty);
                    bool foundModel = false;
                    bool foundR = false;

                    foreach (var region in cardRegions)
                    {
                        string regionText = region.Text ?? "";

                        // 检查是否包含车型名称（归一化比较）
                        if (!foundModel && Normalize7(regionText).Contains(normalizedCardModel))
                        {
                            foundModel = true;
                            ClsLogger.LogScript($"步骤13.5: 识别到车型名称 '{modelName}'");
                        }

                        // 检查是否包含字母R || S2（不区分大小写）
                        if (!foundR && (!foundR && Regex.IsMatch(regionText, @"R\d{3}(?!\d)|S2\d{3}(?!\d)", RegexOptions.IgnoreCase)))
                        {
                            foundR = true;
                            ClsLogger.LogScript($"步骤13.5: 识别到字母R || S2");
                        }

                        if (foundModel && foundR)
                        {
                            break;
                        }
                    }

                    if (foundModel && foundR)
                    {
                        ClsLogger.LogScript($"步骤13.5: 车辆卡片界面当前驾驶的车辆范围已包含车型名称和字母R，跳过制造商选择，直接前往步骤31");
                        skipManufacturerSelection = true;
                    }
                }
                else
                {
                    ClsLogger.LogScript("步骤13.5: 未能识别车辆卡片界面当前驾驶的车辆区域，继续正常流程");
                }

                // 如果不需要跳过制造商选择，则继续原流程
                if (!skipManufacturerSelection)
                {
                    // 步骤7: 点击前往制造商（前往车厂）
                    CheckCancel("步骤14");
                    ClsLogger.LogScript($"步骤14: 点击前往制造商（前往车厂）");
                    if (!TryRecognizeAndClickROI(ClsROI.UIElem.前往制造商, "前往制造商", shouldClick: true, debug: debug))
                    {
                        ClsLogger.LogScript("步骤14: 未能找到或点击'前往制造商'");
                        return;
                    }
                    ClsLogger.LogScript("步骤14: 已点击前往制造商");
                    Thread.Sleep(1000);

                    // 步骤8: 选择车厂并查找指定车型赛车
                    CheckCancel("步骤15");
                    ClsLogger.LogScript($"步骤15: 开始选择 {manufacturerName} 车厂的 {modelName} 赛车");

                    try
                    {
                        FocusWindowByProcessName("forzahorizon6");

                        bool clickedManufacturer = false;
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            CheckCancel($"选车步骤1-{attempt + 1}");
                            if (TryRecognizeAndClickROI(ClsROI.UIElem.整页, manufacturerName, shouldClick: true, debug: debug))
                            {
                                ClsLogger.LogScript($"步骤17: 已点击 {manufacturerName}");
                                clickedManufacturer = true;
                                break;
                            }

                            if (attempt == 0)
                            {
                                ClsLogger.LogScript("步骤18: 未找到指定制造商，执行 PageUp 后重试");
                                FocusWindowByProcessName("forzahorizon6");
                                ClickKeyAndWait(Key.PageUp, 1000, nameof(GotoScriptRace));
                            }
                        }

                        if (!clickedManufacturer)
                        {
                            ClsLogger.LogScript("步骤20: 未找到指定制造商，取消任务");
                            return;
                        }

                        Thread.Sleep(800);

                        // 在车厂界面查找符合车型的赛车并按性能分选择最高
                        for (int round = 0; round < 20; round++)
                        {
                            CheckCancel($"选车步骤2-{round + 1}");
                            if (!TryGetObsScreenshotMat(out Mat carFactoryMat))
                            {
                                ClsLogger.LogScript("步骤21: 获取车厂界面截图失败");
                                return;
                            }

                            using (carFactoryMat)
                            {
                                DebugShow(carFactoryMat, $"Step7 Factory Screen (round {round + 1})", debug);
                                if (!TryRecognizeAndClickROI(
                                    carFactoryMat,
                                    new OpenCvSharp.Rect(0, 0, carFactoryMat.Width, carFactoryMat.Height),
                                    out var factoryRegions,
                                    searchText: null,
                                    shouldClick: false,
                                    debug: debug,
                                    debugTitle: $"GotoScriptRace 工厂页OCR (round {round + 1})"))
                                {
                                    ClsLogger.LogScript("步骤22: 车厂界面OCR识别失败");
                                    return;
                                }

                                var normalizedModel = Normalize7(modelName ?? string.Empty);

                                if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车辆框, out OpenCvSharp.Rect carFrameBaseRect))
                                {
                                    ClsLogger.LogScript("步骤23: ROI 中未配置 '车辆框'");
                                    return;
                                }

                                var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                                var targetSize = new OpenCvSharp.Size(carFactoryMat.Width, carFactoryMat.Height);
                                OpenCvSharp.Rect carFrameScaled = ClsROI.ScaleFromBase(carFrameBaseRect, baseResolution, targetSize);

                                if (!TryCreateSafeCropRect(carFactoryMat, carFrameScaled, out OpenCvSharp.Rect safeCarFrame))
                                {
                                    ClsLogger.LogScript("步骤24: '车辆框' 缩放后无效");
                                    return;
                                }

                                var modelRegions = (factoryRegions ?? new List<PaddleOcrResultRegion>())
                                    .Where(p =>
                                    {
                                        var t = Normalize7(p.Text ?? string.Empty);
                                        if (string.IsNullOrEmpty(normalizedModel) || !t.Contains(normalizedModel)) return false;
                                        if (p.Score <= 0) return false;
                                        return true;
                                    })
                                    .OrderBy(p => p.Rect.Center.X)
                                    .ThenBy(p => p.Rect.Center.Y)
                                    .ToList();

                                if (modelRegions.Count == 0)
                                {
                                    ClsLogger.LogScript($"步骤25: 第{round + 1}轮未找到车型 '{modelName}'，翻页继续");
                                    ClickKeyAndWait(Key.Right, 500, nameof(GotoScriptRace));
                                    continue;
                                }

                                var racecarsWithPerformance = new List<(int centerX, int centerY, int performanceScore)>();

                                for (int i = 0; i < modelRegions.Count; i++)
                                {
                                    var modelRegion = modelRegions[i];
                                    RotatedRect modelRect = modelRegion.Rect;
                                    int modelCenterX = (int)Math.Round(modelRect.Center.X);
                                    int modelCenterY = (int)Math.Round(modelRect.Center.Y);

                                    int expW = safeCarFrame.Width;
                                    int expH = safeCarFrame.Height;
                                    int expX = modelCenterX - expW / 2;
                                    int marginFromTop = (int)Math.Round(expH * 0.06);
                                    int expY = modelCenterY - marginFromTop;

                                    if (expX < 0) expX = 0;
                                    if (expY < 0) expY = 0;
                                    if (expX + expW > carFactoryMat.Width) expX = Math.Max(0, carFactoryMat.Width - expW);
                                    if (expY + expH > carFactoryMat.Height) expY = Math.Max(0, carFactoryMat.Height - expH);

                                    if (expW <= 0 || expH <= 0)
                                    {
                                        expX = safeCarFrame.X;
                                        expY = safeCarFrame.Y;
                                        expW = safeCarFrame.Width;
                                        expH = safeCarFrame.Height;
                                    }

                                    OpenCvSharp.Rect expandedRect = new OpenCvSharp.Rect(expX, expY, expW, expH);

                                    if (!TryCreateSafeCropRect(carFactoryMat, expandedRect, out OpenCvSharp.Rect safeExpandedRect))
                                    {
                                        continue;
                                    }

                                    using Mat expandedCropped = new Mat(carFactoryMat, safeExpandedRect);
                                    DebugShow(expandedCropped, $"Step7 Expanded ROI ({i + 1})", debug);
                                    if (!TryRecognizeAndClickROI(
                                        carFactoryMat,
                                        safeExpandedRect,
                                        out var expandedRegions,
                                        searchText: null,
                                        shouldClick: false,
                                        debug: debug,
                                        debugTitle: $"Step7 Expanded ROI ({i + 1})"))
                                    {
                                        continue;
                                    }

                                    int performanceScore = 0;
                                    foreach (var region in expandedRegions)
                                    {
                                        if (region.Score <= 0) continue;

                                        var matches = System.Text.RegularExpressions.Regex.Matches(region.Text ?? string.Empty, @"\d+");
                                        foreach (System.Text.RegularExpressions.Match m in matches)
                                        {
                                            if (int.TryParse(m.Value, out int score) && score > performanceScore)
                                            {
                                                performanceScore = score;
                                                ClsLogger.LogScript($"步骤26: 车型候选 {i + 1} 识别到性能分: {score} (OCR文本: {region.Text}, 信度: {region.Score:F2})");
                                            }
                                        }
                                    }

                                    if (performanceScore <= 0)
                                    {
                                        ClsLogger.LogScript($"步骤26: 车型候选 {i + 1} 未能识别到有效的性能分，记录为 0");
                                    }

                                    racecarsWithPerformance.Add((modelCenterX, modelCenterY, performanceScore));
                                    ClsLogger.LogScript($"步骤26: 车型候选 {i + 1} 最终性能分: {performanceScore}");
                                }

                                if (racecarsWithPerformance.Count == 0)
                                {
                                    ClsLogger.LogScript("步骤27: 未能提取任何候选赛车性能分，取消任务");
                                    return;
                                }

                                var best = racecarsWithPerformance.OrderByDescending(r => r.performanceScore).First();
                                CheckCancel("选车步骤3");

                                if (best.performanceScore <= 0)
                                {
                                    ClsLogger.LogScript($"步骤28: 警告 - 最优候选赛车性能分为 0，仍将继续选择（模型: {modelName}）");
                                }

                                if (!TryClickImagePoint(carFactoryMat, best.centerX, best.centerY, $"点击性能分最高的{modelName}赛车"))
                                {
                                    return;
                                }
                                ClsLogger.LogScript($"步骤28: 已选择性能最高的{modelName}赛车 (性能分: {best.performanceScore})，共检测 {racecarsWithPerformance.Count} 个候选");
                                break;
                            }
                        }

                        Thread.Sleep(1000);
                    }
                    catch (OperationCanceledException)
                    {
                        ClsLogger.LogScript("步骤29: 操作被用户取消");
                        return;
                    }
                    catch (Exception ex)
                    {
                        ClsLogger.LogScript($"步骤30: 发生错误 - {ex.Message}");
                        return;
                    }
                } // 结束 if (!skipManufacturerSelection) 的大括号

                // 步骤31: 按 Enter (等 2 秒) → Enter (等 2 秒) → Enter
                CheckCancel("步骤31");
                ClsLogger.LogScript("步骤31: 按 Enter/等2秒/Enter/等2秒/Enter");
                ClickKeyAndWait(Key.Enter, 2000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 2000, nameof(GotoScriptRace));
                ClickKeyAndWait(Key.Enter, 500, nameof(GotoScriptRace));

                // 步骤32: 等待 15 秒，然后按 Enter
                CheckCancel("步骤32");
                ClsLogger.LogScript("步骤32: 等待 15 秒");
                Thread.Sleep(15000);
                ClsLogger.LogScript("步骤33: 按 Enter");
                ClickKeyAndWait(Key.Enter, 500, nameof(GotoScriptRace));
                // 步骤34-44: 循环检测重新开始按钮，根据计算的循环次数执行
                int currentPoints = techPoints;
                bool raceComplete = false;
                int loopCount = 0; // 循环计数
                Stopwatch restartDetectTimeout = Stopwatch.StartNew();

                while (!raceComplete && loopCount < loopsRequired && restartDetectTimeout.ElapsedMilliseconds < restartDetectTimeoutMs)
                {
                    loopCount++;
                    ClsLogger.LogScript($"步骤34: 开始第 {loopCount}/{loopsRequired} 次循环");
                    CheckCancel("步骤34");

                    // 步骤34: 按下 W，并循环检测重新开始按钮
                    ClsLogger.LogScript("步骤34: 按下 W");
                    Thread.Sleep(1000);
                    ClsLogicContorl_Ghub.KeyDown(ClsLogicContorl_Ghub.ToGhubKey(Key.W));

                    bool restartButtonDetected = false;
                    Stopwatch detectStopwatch = Stopwatch.StartNew();
                    const int detectTimeoutMs = 120000; // 2分钟内检测

                    while (!restartButtonDetected && detectStopwatch.ElapsedMilliseconds < detectTimeoutMs)
                    {
                        CheckCancel("步骤35");
                        Thread.Sleep(restartDetectIntervalMs);

                        // 检测"重新开始"按钮 - 使用新的公共方法
                        if (TryRecognizeAndClickROI(ClsROI.UIElem.重新开始, "重新开始", shouldClick: false, debug: debug))
                        {
                            restartButtonDetected = true;
                            ClsLogger.LogScript("步骤36: 检测到'重新开始'按钮，重置总超时计时器");
                            // 检测到重新开始按钮后，重置总超时计时器
                            restartDetectTimeout.Restart();
                            break;
                        }
                    }

                    // 弹起 W
                    ClsLogger.LogScript("步骤37: 弹起 W");
                    ClsLogicContorl_Ghub.KeyUp(ClsLogicContorl_Ghub.ToGhubKey(Key.W));
                    Thread.Sleep(200);

                    if (!restartButtonDetected)
                    {
                        ClsLogger.LogScript("步骤38: 2分钟内未检测到重新开始按钮，任务超时");
                        break;
                    }

                    // 点数加上单局点数并检查是否完成
                    currentPoints += pointsPerRace;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { DetectPoint?.Invoke(null, currentPoints); });
                    ClsLogger.LogScript($"步骤39: 技术点数更新为 {currentPoints}");

                    // 检查是否达到目标或已完成所有循环
                    if (loopCount >= loopsRequired)
                    {
                        // 已完成所有必需的循环
                        CheckCancel("步骤40");
                        ClsLogger.LogScript($"步骤40: 已完成 {loopCount} 次循环，点数: {currentPoints}，按 Enter 等待 20 秒");
                        ClickKeyAndWait(Key.Enter, 20000, nameof(GotoScriptRace));

                        ClsLogger.LogScript("步骤41: 触发完成事件");
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { PointCompletionCompleted?.Invoke(null, EventArgs.Empty); });
                        finished = true;
                        raceComplete = true;
                    }
                    else
                    {
                        // 还需继续循环
                        CheckCancel("步骤42");
                        ClsLogger.LogScript($"步骤42: 循环 {loopCount}/{loopsRequired}，点数 {currentPoints}，继续任务");

                        // 触发点数变更事件（直接在流程中触发）
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { DetectPoint?.Invoke(null, currentPoints); });

                        // 按 X 等待 400ms
                        ClickKeyAndWait(Key.X, 400, nameof(GotoScriptRace));

                        // 按 Enter
                        ClickKeyAndWait(Key.Enter, 100, nameof(GotoScriptRace));

                        // 等待 8 秒
                        ClsLogger.LogScript("步骤43: 等待 8 秒");
                        Thread.Sleep(8000);

                        // 按 Enter
                        ClickKeyAndWait(Key.Enter, 300, nameof(GotoScriptRace));

                        // 继续循环
                        ClsLogger.LogScript("步骤44: 继续循环，返回检测流程");
                    }
                }

                if (!raceComplete && restartDetectTimeout.ElapsedMilliseconds >= restartDetectTimeoutMs)
                {
                    ClsLogger.LogScript("=== GotoScriptRace 总超时（2分钟），任务结束 ===");
                }
                else if (!raceComplete && loopCount >= loopsRequired)
                {
                    ClsLogger.LogScript($"=== GotoScriptRace 已完成 {loopCount} 次循环，但未触发完成事件 ===");
                }
                else if (raceComplete)
                {
                    ClsLogger.LogScript("=== GotoScriptRace 成功完成 ===");
                }
            }
            catch (OperationCanceledException)
            {
                ClsLogger.LogScript("GotoScriptRace: 操作被用户取消");
            }
            catch (Exception ex)
            {
                ClsLogger.LogScript($"GotoScriptRace: 发生异常 - {ex.Message}");
                Debug.WriteLine($"GotoScriptRace Exception: {ex}");
            }
            finally
            {
                if (!finished)
                {
                    try { System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { PointCompletionCompleted?.Invoke(null, EventArgs.Empty); }); } catch { }
                }
            }
        }

        #endregion

        #region Win32API
        // ShowWindow 的参数常量
        private const int SW_RESTORE = 9;

        /// <summary>
        /// 将指定窗口设置为前台窗口（将其激活）。
        /// 这是对 Win32 SetForegroundWindow 函数的 P/Invoke 封装。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 显示或隐藏指定窗口。对应 Win32 ShowWindow。
        /// nCmdShow 可指定恢复最小化窗口等行为。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// 判断指定窗口是否被最小化（图标化）。对应 Win32 IsIconic。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        /// <summary>
        /// 获取当前处于前台的窗口句柄（Win32 GetForegroundWindow）。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 获取窗口所属线程 ID 和进程 ID（Win32 GetWindowThreadProcessId）。
        /// 返回线程 ID，out 参数返回进程 ID。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// 获取当前线程 ID（Win32 GetCurrentThreadId）。
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// 将两个线程的输入处理附加或分离，用于临时将当前线程与目标窗口线程关联以便设置焦点。
        /// 对应 Win32 AttachThreadInput。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        /// <summary>
        /// 将指定窗口置于顶部（不一定激活）。对应 Win32 BringWindowToTop。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        /// <summary>
        /// 设置活动窗口（不一定改变键盘输入焦点）。对应 Win32 SetActiveWindow。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        /// <summary>
        /// 设置键盘输入焦点到指定窗口或控件。对应 Win32 SetFocus。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        /// <summary>
        /// 获取指定窗口的客户区矩形（相对于窗口客户区的坐标）。对应 Win32 GetClientRect。
        /// 返回的 RECT 表示客户区左、上、右、下边界（以像素为单位）。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRECT lpRect);

        /// <summary>
        /// 将指定窗口客户区内的点从客户区坐标转换为屏幕坐标（对应 Win32 ClientToScreen）。
        /// lpPoint 为输入输出参数，调用后包含转换到屏幕坐标系的点位置。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePOINT lpPoint);

        /// <summary>
        /// 获取窗口在屏幕坐标系下的矩形（左上角和右下角）。对应 Win32 GetWindowRect。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll")]
        private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        // 引入控制输入法状态的 Win32 API
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetConversionStatus(IntPtr hIMC, ref int conversion, ref int sentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetConversionStatus(IntPtr hIMC, int conversion, int sentence);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, int wParam, int lParam);


        private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const int HKL_ENGLISH_US = 0x04090409; // 纯英文美式键盘布局

        /// <summary>
        /// 表示一个二维点（与 Win32 POINT 结构等价），使用像素为单位。
        /// 字段 x、y 分别为水平和垂直坐标。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePOINT
        {
            public int x;
            public int y;
        }

        /// <summary>
        /// 表示一个矩形（与 Win32 RECT 结构等价），使用像素为单位。
        /// 字段 left、top、right、bottom 分别表示矩形的左、上、右、下边界。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        #endregion

        #region 窗口相关方法

        /// <summary>
        /// 从 OBS 获取当前画面截图并解码为 OpenCvSharp.Mat。
        /// 同步调用 ClsObs 接口获取 Base64 字符串并解码为 Mat。
        /// 返回 true 表示成功并通过 out 参数返回有效的 Mat。
        /// </summary>
        /// <param name="imageMat">输出：解码得到的截图 Mat（需由调用方释放）。</param>
        /// <returns>是否成功获取并解码截图。</returns>
        private static bool TryGetObsScreenshotMat(out Mat imageMat)
        {
            imageMat = new Mat();
            try
            {
                var sources = ClsObs._obs.GetCurrentProgramScene();
                string? base64Image = ClsObs.GetSourceScreenshotAsync(sources, "png", ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight, 100).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(base64Image))
                {
                    Debug.WriteLine("TryGetObsScreenshotMat: 未能从 OBS 获取截图（base64 为空）。");
                    return false;
                }

                byte[] gameShot = Convert.FromBase64String(base64Image);
                imageMat = Cv2.ImDecode(gameShot, ImreadModes.Color);
                return imageMat != null && !imageMat.Empty();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryGetObsScreenshotMat: 获取或解码截图失败: {ex.Message}");
                try { imageMat?.Dispose(); } catch { }
                imageMat = new Mat();
                return false;
            }
        }

        /// <summary>
        /// 根据给定的 sourceRect 计算一个保证在 imageMat 范围内的裁剪矩形，避免越界。
        /// </summary>
        private static bool TryCreateSafeCropRect(Mat imageMat, OpenCvSharp.Rect sourceRect, out OpenCvSharp.Rect cropRect)
        {
            cropRect = new OpenCvSharp.Rect();
            if (imageMat == null || imageMat.Empty()) return false;
            int cropX = Math.Max(0, sourceRect.X);
            int cropY = Math.Max(0, sourceRect.Y);
            int cropW = Math.Max(0, Math.Min(sourceRect.Width, imageMat.Width - cropX));
            int cropH = Math.Max(0, Math.Min(sourceRect.Height, imageMat.Height - cropY));
            cropRect = new OpenCvSharp.Rect(cropX, cropY, cropW, cropH);
            return cropW > 0 && cropH > 0;
        }

        /// <summary>
        /// 将 Mat 编码为 PNG 字节数组，失败时返回 false。
        /// </summary>
        private static bool TryEncodeMatAsPng(Mat mat, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                Cv2.ImEncode(".png", mat, out bytes);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryEncodeMatAsPng: 编码失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将图片坐标系的点映射到窗口屏幕坐标系，返回缩放系数与目标坐标。
        /// </summary>
        private static void MapImagePointToWindow(Mat imageMat, OpenCvSharp.Rect wndRect, double pointX, double pointY, out double scaleX, out double scaleY, out int targetX, out int targetY)
        {
            // 保持宽高比等比缩放，并在窗口区域内居中图片（处理 letterbox/pillarbox），
            // 与 UpCarPoint 中的点击映射逻辑保持一致，避免窗口高度变小时出现向上偏移。
            double sx = imageMat.Width > 0 ? wndRect.Width / (double)imageMat.Width : 1.0;
            double sy = imageMat.Height > 0 ? wndRect.Height / (double)imageMat.Height : 1.0;
            double scale = Math.Min(sx, sy);

            double scaledImgW = imageMat.Width * scale;
            double scaledImgH = imageMat.Height * scale;

            double offsetX = wndRect.X + Math.Max(0.0, (wndRect.Width - scaledImgW) / 2.0);
            double offsetY = wndRect.Y + Math.Max(0.0, (wndRect.Height - scaledImgH) / 2.0);

            scaleX = scale;
            scaleY = scale;

            targetX = (int)Math.Round(offsetX + pointX * scale);
            targetY = (int)Math.Round(offsetY + pointY * scale);
        }

        /// <summary>
        /// 在窗口中将图片坐标映射为屏幕坐标并执行点击（统一实现，供各处调用以保证一致性）。
        /// 返回 true 表示已执行点击，false 表示未能定位窗口或失败。
        /// </summary>
        private static bool TryClickImagePoint(Mat srcMat, double imageX, double imageY, string logTag)
        {
            try
            {
                FocusWindowByProcessName("forzahorizon6");
                if (!TryGetWindowRectByProcessName("forzahorizon6", out OpenCvSharp.Rect wndRect))
                {
                    ClsLogger.LogPoint($"{logTag} 失败，未能获取游戏窗口位置。");
                    return false;
                }

                MapImagePointToWindow(srcMat, wndRect, imageX, imageY, out _, out _, out int clickX, out int clickY);
                ClsLogicContorl_Ghub.Move(-4096, -4096);
                ClsLogicContorl_Ghub.Move(clickX, clickY, true);
                Thread.Sleep(100);
                // 使用统一的点击与等待封装，便于日志与行为一致
                ClickMouseAndWait(1, 100, logTag);
                Debug.WriteLine($"x = {clickX} y = {clickY}");
                return true;
            }
            catch (Exception ex)
            {
                ClsLogger.LogPoint($"{logTag} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将指定进程名对应的主窗口尝试置于前台并取得输入焦点。
        /// 实现细节：尝试通过 AttachThreadInput 将当前线程与目标窗口线程/前台线程关联，
        /// 并调用 ShowWindow/BringWindowToTop/SetForegroundWindow/SetActiveWindow/SetFocus 等 API。
        /// 该方法适用于需要在自动化操作前确保目标窗口可接收键盘/鼠标输入的场景。
        /// </summary>
        /// <param name="processName">目标进程名（不含扩展名），如 "forzahorizon6"。</param>
        public static void FocusWindowByProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return;


            var processes = Process.GetProcessesByName(processName);
            foreach (var p in processes)
            {
                var h = IntPtr.Zero;
                try
                {
                    h = p.MainWindowHandle;
                }
                catch
                {
                    continue;
                }
                if (h == IntPtr.Zero) continue;

                // Get thread ids
                var fg = GetForegroundWindow();
                uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
                uint targetThread = GetWindowThreadProcessId(h, out _);
                uint currentThread = GetCurrentThreadId();

                bool attachedFg = false;
                bool attachedTarget = false;
                try
                {
                    // Attach current thread input to foreground and target threads to allow setting focus reliably
                    if (fgThread != 0 && fgThread != currentThread)
                    {
                        AttachThreadInput(currentThread, fgThread, true);
                        attachedFg = true;
                    }

                    if (targetThread != 0 && targetThread != currentThread)
                    {
                        AttachThreadInput(currentThread, targetThread, true);
                        attachedTarget = true;
                    }

                    if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                    // Try several calls to ensure window is brought to front and receives input focus
                    BringWindowToTop(h);
                    SetForegroundWindow(h);
                    SetActiveWindow(h);
                    SetFocus(h);

                    return;
                }
                finally
                {
                    //输入法切换为英文
                    SetImeToEnglish();

                    if (attachedTarget)
                    {

                        AttachThreadInput(currentThread, targetThread, false);
                    }
                    if (attachedFg)
                    {
                        AttachThreadInput(currentThread, fgThread, false);
                    }
                }
            }
        }
        // 尝试根据进程名获取窗口位置（像素坐标）
        /// <summary>
        /// 尝试根据进程名查找主窗口并返回其屏幕坐标矩形（像素）。
        /// 返回值为 true 表示成功并通过 out 参数返回窗口矩形；false 表示未找到或失败。
        /// </summary>
        /// <param name="processName">目标进程名（无扩展名）。</param>
        /// <param name="rect">输出：窗口在屏幕上的矩形（左上角坐标与宽高）。</param>
        /// <returns>是否成功获取窗口矩形。</returns>
        public static bool TryGetWindowRectByProcessName(string processName, out OpenCvSharp.Rect rect)
        {
            rect = new OpenCvSharp.Rect();
            if (string.IsNullOrEmpty(processName)) return false;

            var processes = Process.GetProcessesByName(processName);
            foreach (var p in processes)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = p.MainWindowHandle;
                }
                catch
                {
                    continue;
                }
                if (h == IntPtr.Zero) continue;

                if (GetClientRect(h, out NativeRECT clientRect))
                {
                    var origin = new NativePOINT { x = 0, y = 0 };
                    ClientToScreen(h, ref origin);
                    rect = new OpenCvSharp.Rect(origin.x, origin.y, clientRect.right - clientRect.left, clientRect.bottom - clientRect.top);
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// 强制将当前的中文输入法切换为【英文】状态（等同于按下Shift）
        /// </summary>
        public static void SetImeToEnglish()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                // 直接对窗口句柄投递消息，不需要 ImmGetContext
                PostMessage(hWnd, WM_INPUTLANGCHANGEREQUEST, 0, HKL_ENGLISH_US);
            }
        }

        /// <summary>
        /// 将在 OBS 截图坐标系下定义的矩形按截图与窗口的缩放比例映射到窗口屏幕坐标系并返回对应的 RECT。
        /// 参数说明：manualRect 为在 obsMat 图像坐标系下的矩形；windowRect 为目标窗口在屏幕上的 RECT；obsMat 为 OBS 截图 Mat。
        /// 返回的 RECT 已经被约束在 windowRect 范围内。
        /// </summary>
        /// <summary>
        /// 将在 OBS 截图坐标系下定义的矩形按截图与窗口的缩放比例映射到窗口屏幕坐标系并返回对应的 RECT。
        /// 参数说明：manualRect 为在 obsMat 图像坐标系下的矩形；windowRect 为目标窗口在屏幕上的 RECT；obsMat 为 OBS 截图 Mat。
        /// 返回的 RECT 已经被约束在 windowRect 范围内。
        /// </summary>
        /// <param name="manualRect">来源于 OBS 截图坐标系的矩形。</param>
        /// <param name="windowRect">目标窗口在屏幕坐标系下的矩形。</param>
        /// <param name="obsMat">OBS 提供的截图 Mat，用于计算缩放系数。</param>
        /// <returns>映射到窗口屏幕坐标系的矩形（或空矩形表示无效）。</returns>
        public static OpenCvSharp.Rect ScaleRectToWindow(OpenCvSharp.Rect manualRect, OpenCvSharp.Rect windowRect, Mat obsMat)
        {
            OpenCvSharp.Rect result = new OpenCvSharp.Rect();

            if (obsMat == null || obsMat.Width <= 0 || obsMat.Height <= 0)
            {
                return result;
            }

            double scaleX = windowRect.Width / (double)obsMat.Width;
            double scaleY = windowRect.Height / (double)obsMat.Height;

            int scaledX = windowRect.X + (int)Math.Round(manualRect.X * scaleX);
            int scaledY = windowRect.Y + (int)Math.Round(manualRect.Y * scaleY);
            int scaledW = (int)Math.Round(manualRect.Width * scaleX);
            int scaledH = (int)Math.Round(manualRect.Height * scaleY);

            // 将结果约束到窗口范围内
            int left = Math.Max(windowRect.X, scaledX);
            int top = Math.Max(windowRect.Y, scaledY);
            int right = Math.Min(windowRect.X + windowRect.Width, scaledX + scaledW);
            int bottom = Math.Min(windowRect.Y + windowRect.Height, scaledY + scaledH);

            if (right <= left || bottom <= top)
                return new OpenCvSharp.Rect();

            result = new OpenCvSharp.Rect(left, top, right - left, bottom - top);
            return result;
        }
        #endregion
    }
}
