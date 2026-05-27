using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System.Collections;

namespace FH_WPF
{
    /// <summary>
    /// OBS WebSocket 操作封装类。
    /// 包含连接管理、自动重连、场景/录制/推流控制以及状态事件转发。
    /// </summary>
    internal static class ClsObs
    {
        #region 字段
        /// <summary>
        /// OBS WebSocket 客户端实例。
        /// </summary>
        public static readonly OBSWebsocket _obs = new();
        /// <summary>
        /// 连接互斥锁，防止并发连接请求。
        /// </summary>
        private static readonly SemaphoreSlim _connectLock = new(1, 1);

        /// <summary>
        /// 自动重连取消令牌源。
        /// </summary>
        private static CancellationTokenSource? _reconnectCts;
        /// <summary>
        /// 自动重连任务。
        /// </summary>
        private static Task? _reconnectTask;

        /// <summary>
        /// OBS 地址（用于初次连接与自动重连）。
        /// </summary>
        private static string? _ip;
        /// <summary>
        /// OBS 端口。
        /// </summary>
        private static int _port;
        /// <summary>
        /// OBS 连接密码。
        /// </summary>
        private static string? _password;

        /// <summary>
        /// 当前是否处于主动关闭流程。
        /// </summary>
        private static bool _isClosing;
        /// <summary>
        /// 当前是否允许自动重连。
        /// </summary>
        private static bool _shouldReconnect;
        #endregion

        #region 属性
        /// <summary>
        /// 当前是否已连接到 OBS。
        /// </summary>
        public static bool IsConnected => _obs.IsConnected;
        /// <summary>
        /// 当前录制状态（由状态刷新与事件共同维护）。
        /// </summary>
        public static bool IsRecording { get; private set; }
        /// <summary>
        /// 当前推流状态（由状态刷新与事件共同维护）。
        /// </summary>
        public static bool IsStreaming { get; private set; }
        /// <summary>
        /// 从 OBS 视频设置获取的截图目标宽度（像素）。
        /// 连接 OBS 后自动同步，也可手动覆盖。
        /// </summary>
        public static int ScreenshotWidth { get; set; } = 1360;
        /// <summary>
        /// 从 OBS 视频设置获取的截图目标高度（像素）。
        /// 连接 OBS 后自动同步，也可手动覆盖。
        /// </summary>
        public static int ScreenshotHeight { get; set; } = 768;
        /// <summary>
        /// 当前节目场景名称。
        /// </summary>
        public static string CurrentSceneName { get; private set; } = string.Empty;

        /// <summary>
        /// 自动重连间隔，默认 5 秒。
        /// </summary>
        public static TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);
        /// <summary>
        /// 日志回调，默认写入 ClsLogger。
        /// </summary>
        public static Action<string>? Logger { get; set; } = ClsLogger.Log;
        #endregion

        #region 事件
        /// <summary>
        /// 连接成功事件。
        /// </summary>
        public static event Action? OnConnected;
        /// <summary>
        /// 断开连接事件。
        /// </summary>
        public static event Action? OnDisconnected;
        /// <summary>
        /// 录制状态变化事件，参数为是否录制中。
        /// </summary>
        public static event Action<bool>? OnRecordStateChanged;
        /// <summary>
        /// 推流状态变化事件，参数为是否推流中。
        /// </summary>
        public static event Action<bool>? OnStreamStateChanged;
        /// <summary>
        /// 场景变化事件，参数为当前场景名。
        /// </summary>
        public static event Action<string>? OnSceneChanged;
        #endregion

        #region 构造
        static ClsObs()
        {
            // 绑定底层 OBS 事件并转发为当前类的状态与事件。
            _obs.Connected += ObsOnConnected;
            _obs.Disconnected += ObsOnDisconnected;
            _obs.RecordStateChanged += ObsOnRecordStateChanged;
            _obs.StreamStateChanged += ObsOnStreamStateChanged;
            _obs.CurrentProgramSceneChanged += ObsOnCurrentProgramSceneChanged;
        }
        #endregion

        #region 公共业务方法
        /// <summary>
        /// 连接 OBS，并保存连接参数用于自动重连。
        /// </summary>
        public static async Task<bool> ConnectAsync(string ip, int port, string password, CancellationToken cancellationToken = default)
        {
            _ip = ip;
            _port = port;
            _password = password;
            _isClosing = false;
            _shouldReconnect = true;

            return await TryConnectOnceAsync(cancellationToken);
        }

        /// <summary>
        /// 断开 OBS 连接并停止自动重连。
        /// </summary>
        public static async Task DisconnectAsync()
        {
            _shouldReconnect = false;
            _isClosing = true;
            StopReconnectLoop();

            if (_obs.IsConnected)
            {
                await Task.Run(() => _obs.Disconnect());
            }

            IsRecording = false;
            IsStreaming = false;
            CurrentSceneName = string.Empty;
        }

        /// <summary>
        /// 切换节目场景。
        /// </summary>
        public static async Task<bool> SwitchSceneAsync(string sceneName)
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.SetCurrentProgramScene(sceneName));
                Log($"切换场景成功: {sceneName}");
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"切换场景失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取场景名称列表。
        /// </summary>
        public static async Task<IReadOnlyList<string>> GetSceneListAsync()
        {
            try
            {
                EnsureConnected();
                var raw = await Task.Run(() => _obs.GetSceneList());
                var result = ExtractSceneNames(raw);
                return result;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"获取场景列表失败: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 开始录制。
        /// </summary>
        public static async Task<bool> StartRecordAsync()
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.StartRecord());
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"开始录制失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止录制。
        /// </summary>
        public static async Task<bool> StopRecordAsync()
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.StopRecord());
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"停止录制失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换录制状态。
        /// </summary>
        public static async Task<bool> ToggleRecordAsync()
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.ToggleRecord());
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"切换录制失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 开始推流。
        /// </summary>
        public static async Task<bool> StartStreamAsync()
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.StartStream());
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"开始推流失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止推流。
        /// </summary>
        public static async Task<bool> StopStreamAsync()
        {
            try
            {
                EnsureConnected();
                await Task.Run(() => _obs.StopStream());
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"停止推流失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置指定场景中某个源的可见性。
        /// </summary>
        public static async Task<bool> SetSourceVisibilityAsync(string sceneName, string sourceName, bool isVisible)
        {
            try
            {
                EnsureConnected();
                var sceneItemsRaw = await Task.Run(() => _obs.GetSceneItemList(sceneName));
                if (!TryFindSceneItemId(sceneItemsRaw, sourceName, out var sceneItemId))
                {
                    Log($"未找到源: scene={sceneName}, source={sourceName}");
                    return false;
                }

                await Task.Run(() => _obs.SetSceneItemEnabled(sceneName, sceneItemId, isVisible));
                Log($"设置源可见性成功: scene={sceneName}, source={sourceName}, visible={isVisible}");
                return true;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"设置源可见性失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取源截图（返回纯 Base64，自动去除 data URL 前缀）。
        /// </summary>
        public static async Task<string?> GetSourceScreenshotAsync(string sourceName, string imageFormat = "png", int width = 0, int height = 0, int quality = 100)
        {
            try
            {
                EnsureConnected();
                var dataUrlOrBase64 = _obs.GetSourceScreenshot(sourceName, imageFormat, width, height, quality);
                if (string.IsNullOrWhiteSpace(dataUrlOrBase64))
                {
                    return null;
                }

                var commaIndex = dataUrlOrBase64.IndexOf(',');
                return commaIndex >= 0 ? dataUrlOrBase64[(commaIndex + 1)..] : dataUrlOrBase64;
            }
            catch (OBSNotConnectedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"获取截图失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Base64 字符串转字节数组。
        /// </summary>
        public static byte[]? Base64ToBytes(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region 连接与状态刷新
        private static async Task<bool> TryConnectOnceAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_ip) || _port <= 0)
            {
                return false;
            }

            await _connectLock.WaitAsync(cancellationToken);
            try
            {
                if (_obs.IsConnected)
                {
                    return true;
                }

                var url = BuildObsWebSocketUrl(_ip, _port);
                Log($"开始连接 OBS: {url}");
                //await Task.Run(() => _obs.ConnectAsync(url, _password ?? string.Empty), cancellationToken);
                _obs.ConnectAsync(url, _password ?? string.Empty);


                Log($"OBS 连接{(_obs.IsConnected ? "成功" : "失败")}");
                return _obs.IsConnected;
            }
            catch (Exception ex)
            {
                Log($"OBS 连接失败: {ex.Message}");
                return false;
            }
            finally
            {
                _connectLock.Release();
            }
        }

        /// <summary>
        /// 刷新本地缓存的场景、录制、推流状态。
        /// </summary>
        private static async Task RefreshStateAsync()
        {
            try
            {
                var currentSceneRaw = await Task.Run(() => _obs.GetCurrentProgramScene());
                var currentScene = ExtractSceneName(currentSceneRaw);
                if (!string.IsNullOrWhiteSpace(currentScene))
                {
                    CurrentSceneName = currentScene;
                }
            }
            catch (Exception ex)
            {
                Log($"刷新场景状态失败: {ex.Message}");
            }

            try
            {
                var recordStatus = await Task.Run(() => _obs.GetRecordStatus());
                if (TryExtractOutputActive(recordStatus, out var recording))
                {
                    IsRecording = recording;
                    OnRecordStateChanged?.Invoke(IsRecording);
                }
            }
            catch (Exception ex)
            {
                Log($"刷新录制状态失败: {ex.Message}");
            }

            try
            {
                var streamStatus = await Task.Run(() => _obs.GetStreamStatus());
                if (TryExtractOutputActive(streamStatus, out var streaming))
                {
                    IsStreaming = streaming;
                    OnStreamStateChanged?.Invoke(IsStreaming);
                }
            }
            catch (Exception ex)
            {
                Log($"刷新推流状态失败: {ex.Message}");
            }
        }
        #endregion

        #region OBS 事件处理
        private static void ObsOnConnected(object? sender, EventArgs e)
        {
            Log("收到 OBS Connected 事件");
            StopReconnectLoop();
            try
            {
                var videoSettings = _obs.GetVideoSettings();
                //ScreenshotWidth = videoSettings.BaseWidth;
                //ScreenshotHeight = videoSettings.BaseHeight;
                //Log($"已从 OBS 同步分辨率: {ScreenshotWidth}x{ScreenshotHeight}");
            }
            catch (Exception ex)
            {
                Log($"获取 OBS 视频设置失败，保留默认分辨率: {ex.Message}");
            }
            OnConnected?.Invoke();
        }

        private static void ObsOnDisconnected(object? sender, ObsDisconnectionInfo e)
        {
            Log($"收到 OBS Disconnected 事件: {e}");
            IsRecording = false;
            IsStreaming = false;
            CurrentSceneName = string.Empty;
            OnDisconnected?.Invoke();

            if (!_isClosing && _shouldReconnect)
            {
                StartReconnectLoop();
            }
        }

        private static void ObsOnRecordStateChanged(object? sender, RecordStateChangedEventArgs e)
        {
            if (!TryExtractOutputActive(e.OutputState, out var recording))
            {
                recording = IsRecording;
            }

            IsRecording = recording;
            OnRecordStateChanged?.Invoke(IsRecording);
            Log($"录制状态变化: {e.OutputState}");
        }

        private static void ObsOnStreamStateChanged(object? sender, StreamStateChangedEventArgs e)
        {
            if (!TryExtractOutputActive(e.OutputState, out var streaming))
            {
                streaming = IsStreaming;
            }

            IsStreaming = streaming;
            OnStreamStateChanged?.Invoke(IsStreaming);
            Log($"推流状态变化: {e.OutputState}");
        }

        private static void ObsOnCurrentProgramSceneChanged(object? sender, ProgramSceneChangedEventArgs e)
        {
            CurrentSceneName = e.SceneName ?? string.Empty;
            OnSceneChanged?.Invoke(CurrentSceneName);
            Log($"场景变化: {CurrentSceneName}");
        }
        #endregion

        #region 自动重连
        private static void StartReconnectLoop()
        {
            if (_reconnectTask is { IsCompleted: false })
            {
                return;
            }

            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            _reconnectTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _shouldReconnect && !_obs.IsConnected)
                {
                    try
                    {
                        Log("尝试自动重连 OBS...");
                        var ok = await TryConnectOnceAsync(token);
                        if (ok)
                        {
                            await RefreshStateAsync();
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"自动重连异常: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(ReconnectInterval, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private static void StopReconnectLoop()
        {
            if (_reconnectCts is null)
            {
                return;
            }

            try
            {
                _reconnectCts.Cancel();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _reconnectCts.Dispose();
                _reconnectCts = null;
            }
        }
        #endregion

        #region 工具方法
        private static string BuildObsWebSocketUrl(string ip, int port)
        {
            if (ip.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return ip;
            }

            return $"ws://{ip}:{port}";
        }

        private static void EnsureConnected()
        {
            if (!_obs.IsConnected)
            {
                throw new OBSNotConnectedException("OBS 未连接，请先调用 ConnectAsync。");
            }
        }

        private static void Log(string message)
        {
            Logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ClsObs] {message}");
        }

        private static bool TryFindSceneItemId(object sceneItemsRaw, string sourceName, out int sceneItemId)
        {
            sceneItemId = default;

            foreach (var item in EnumerateSceneItems(sceneItemsRaw))
            {
                var rawName = item?.GetType().GetProperty("SourceName")?.GetValue(item) as string;
                if (!string.Equals(rawName, sourceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var idObj = item?.GetType().GetProperty("ItemId")?.GetValue(item);
                if (idObj is int id)
                {
                    sceneItemId = id;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<object?> EnumerateSceneItems(object raw)
        {
            if (raw is IEnumerable list && raw is not string)
            {
                foreach (var item in list)
                {
                    yield return item;
                }

                yield break;
            }

            var sceneItemsProperty = raw.GetType().GetProperty("SceneItems");
            if (sceneItemsProperty?.GetValue(raw) is IEnumerable sceneItems)
            {
                foreach (var item in sceneItems)
                {
                    yield return item;
                }
            }
        }

        private static IReadOnlyList<string> ExtractSceneNames(object raw)
        {
            IEnumerable<object?> source = raw is IEnumerable list && raw is not string
                ? list.Cast<object?>()
                : ExtractSceneListProperty(raw);

            return source
                .Select(i => i?.GetType().GetProperty("Name")?.GetValue(i) as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToArray();
        }

        private static IEnumerable<object?> ExtractSceneListProperty(object raw)
        {
            var scenesProperty = raw.GetType().GetProperty("Scenes");
            if (scenesProperty?.GetValue(raw) is IEnumerable sceneList)
            {
                foreach (var item in sceneList)
                {
                    yield return item;
                }
            }
        }

        private static string? ExtractSceneName(object raw)
        {
            if (raw is string s)
            {
                return s;
            }

            return raw.GetType().GetProperty("SceneName")?.GetValue(raw) as string
                   ?? raw.GetType().GetProperty("Name")?.GetValue(raw) as string;
        }

        private static bool TryExtractOutputActive(object statusObj, out bool isActive)
        {
            isActive = false;

            var activeProp = statusObj.GetType().GetProperty("OutputActive")
                             ?? statusObj.GetType().GetProperty("Active")
                             ?? statusObj.GetType().GetProperty("IsActive");
            if (activeProp?.GetValue(statusObj) is bool b)
            {
                isActive = b;
                return true;
            }

            var outputStateProp = statusObj.GetType().GetProperty("OutputState")
                                  ?? statusObj.GetType().GetProperty("State");
            if (outputStateProp?.GetValue(statusObj) is OutputState state)
            {
                isActive = IsOutputStateActive(state);
                return true;
            }

            return false;
        }

        private static bool IsOutputStateActive(OutputState state)
        {
            return state == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED
                   || state == OutputState.OBS_WEBSOCKET_OUTPUT_RESUMED
                   || state == OutputState.OBS_WEBSOCKET_OUTPUT_STARTING;
        }
        #endregion

        #region 资源释放
        public static void Dispose()
        {
            _isClosing = true;
            _shouldReconnect = false;
            StopReconnectLoop();

            _obs.Connected -= ObsOnConnected;
            _obs.Disconnected -= ObsOnDisconnected;
            _obs.RecordStateChanged -= ObsOnRecordStateChanged;
            _obs.StreamStateChanged -= ObsOnStreamStateChanged;
            _obs.CurrentProgramSceneChanged -= ObsOnCurrentProgramSceneChanged;

            if (_obs.IsConnected)
            {
                try
                {
                    _obs.Disconnect();
                }
                catch
                {
                    // ignore
                }
            }

            _connectLock.Dispose();
        }

        public static ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
        #endregion
    }

    /// <summary>
    /// 在未连接 OBS 时执行需要连接的操作所抛出的异常。
    /// </summary>
    internal sealed class OBSNotConnectedException : InvalidOperationException
    {
        public OBSNotConnectedException(string message) : base(message)
        {
        }
    }
}
