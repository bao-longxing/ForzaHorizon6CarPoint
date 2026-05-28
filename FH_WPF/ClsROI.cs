using OpenCvSharp;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FH_WPF
{
    /// <summary>
    /// 用于定义游戏中的各种区域（如角色信息区、技能栏、地图等）的矩形坐标和尺寸。提高OCR速度。
    /// </summary>
    public static class ClsROI
    {
        #region 目标字典（动态管理 ROI）
        // 游戏界面中有限的命名元素，作为字典键使用
        public enum UIElem
        {
            整页,
            选项,
            车辆卡片界面顶部我的车辆,
            车库界面我的车辆按钮,
            前往制造商,
            车辆框,
            升级与调教,
            车辆熟练度,
            加点界面熟练度点数,
            车库中当前车型,
            车库品牌,
            大世界安娜,
            技术点数可用,
            重新开始,
            收集簿,
            从车库移除车辆,
            车辆卡片界面当前驾驶的车辆,
        }

        // 用于在运行时存储和管理命名的 ROI 区域（键使用枚举 UIElem）
        public static readonly Dictionary<UIElem, OpenCvSharp.Rect> TargetRects = new Dictionary<UIElem, OpenCvSharp.Rect>();
        #endregion

        /// <summary>
        /// 将基于 baseResolution 的矩形按目标尺寸缩放并返回
        /// </summary>
        /// <param name="baseRect">基于 baseResolution 的矩形</param>
        /// <param name="baseResolution">基准分辨率</param>
        /// <param name="targetSize">目标窗口尺寸（像素）</param>
        /// <returns>按目标尺寸缩放后的矩形</returns>
        public static OpenCvSharp.Rect ScaleFromBase(OpenCvSharp.Rect baseRect, OpenCvSharp.Size baseResolution, OpenCvSharp.Size targetSize)
        {
            if (baseResolution.Width == 0 || baseResolution.Height == 0)
                return new OpenCvSharp.Rect();

            double sx = (double)targetSize.Width / baseResolution.Width;
            double sy = (double)targetSize.Height / baseResolution.Height;

            int x = (int)Math.Round(baseRect.X * sx);
            int y = (int)Math.Round(baseRect.Y * sy);
            int w = (int)Math.Round(baseRect.Width * sx);
            int h = (int)Math.Round(baseRect.Height * sy);

            return new OpenCvSharp.Rect(x, y, w, h);
        }

        /// <summary>
        /// 将源图像坐标系下的矩形按窗口实际尺寸进行映射（缩放并加上窗口偏移）
        /// </summary>
        /// <param name="srcRect">源图像坐标系下的矩形（例如 Mat 中的 ROI）</param>
        /// <param name="srcSize">源图像大小（Mat.Width/Height）</param>
        /// <param name="windowRect">窗口在屏幕或目标坐标系中的实际矩形（包含位置与尺寸）</param>
        /// <returns>映射到 windowRect 坐标系的矩形</returns>
        public static OpenCvSharp.Rect MapRectFromSourceToWindow(OpenCvSharp.Rect srcRect, OpenCvSharp.Size srcSize, OpenCvSharp.Rect windowRect)
        {
            if (srcSize.Width == 0 || srcSize.Height == 0)
                return new OpenCvSharp.Rect();

            double sx = (double)windowRect.Width / srcSize.Width;
            double sy = (double)windowRect.Height / srcSize.Height;

            int x = windowRect.X + (int)Math.Round(srcRect.X * sx);
            int y = windowRect.Y + (int)Math.Round(srcRect.Y * sy);
            int w = (int)Math.Round(srcRect.Width * sx);
            int h = (int)Math.Round(srcRect.Height * sy);

            return new OpenCvSharp.Rect(x, y, w, h);
        }

        /// <summary>
        /// 使用 OpenCv 的 SelectROI 在给定的 Mat 上让用户绘制 ROI，并返回与输入图像 src 相同坐标系下的矩形。
        /// </summary>
        /// <param name="src">OpenCvSharp.Mat（来自 OBS/采集 的帧）</param>
        /// <param name="windowRect">保留参数（兼容旧调用），当前不参与坐标换算</param>
        /// <param name="windowName">选择 ROI 时显示的窗口名（可选）</param>
        /// <param name="showCrosshair">是否显示十字线（SelectROI 参数）</param>
        /// <returns>src 坐标系下的 ROI；若未选择或出错返回空矩形</returns>
        public static OpenCvSharp.Rect SelectAndScaleROI(Mat src, OpenCvSharp.Rect windowRect, string windowName = "Select ROI", bool showCrosshair = true)
        {
            try
            {
                if (src == null || src.Empty())
                    return new OpenCvSharp.Rect();
                // 在图像上绘制提示文字，告知用户如何操作（例如：拖动鼠标选择，按 Enter 确认，按 Esc 取消）
                Mat display = null;
                try
                {
                    display = src.Clone();
                    // Ensure display is 3-channel BGR for reliable GUI display and drawing
                    if (display.Channels() != 3)
                    {
                        try
                        {
                            var tmp = new Mat();
                            Cv2.CvtColor(display, tmp, ColorConversionCodes.GRAY2BGR);
                            display.Dispose();
                            display = tmp;
                        }
                        catch
                        {
                            // ignore conversion error and continue with original
                        }
                    }
                    string tip = "Drag to select ROI, press Enter to confirm, Esc to cancel";
                    // 在左上角绘制半透明背景（使用填充矩形）和白色文字
                    int pad = 6;
                    int baseline = 0;
                    var font = HersheyFonts.HersheySimplex;
                    double scale = Math.Max(0.5, Math.Min(1.0, display.Width / 800.0));
                    var textSize = Cv2.GetTextSize(tip, font, scale, 1, out baseline);
                    var rect = new OpenCvSharp.Rect(8, 8, textSize.Width + pad * 2, textSize.Height + pad * 2);
                    Cv2.Rectangle(display, rect, Scalar.Black, Cv2.FILLED);
                    Cv2.PutText(display, tip, new OpenCvSharp.Point(rect.X + pad, rect.Y + textSize.Height + (pad / 2)), font, scale, Scalar.White, 1);

                    // 使用 OpenCvSharp 的 SelectROI，让用户在 display 上绘制 ROI（返回 OpenCvSharp.Rect）
                    OpenCvSharp.Rect cvRoi = default;
                    try
                    {
                        ClsGameControl.RunOnCvThread(() =>
                        {
                            Cv2.NamedWindow(windowName, WindowFlags.AutoSize);
                            cvRoi = Cv2.SelectROI(windowName, display, showCrosshair, false);
                            Cv2.DestroyWindow(windowName);
                        });
                    }
                    catch
                    {
                        try { Cv2.DestroyAllWindows(); } catch { }
                    }

                    if (cvRoi.Width <= 0 || cvRoi.Height <= 0)
                        return new OpenCvSharp.Rect();

                    // 直接返回 src 坐标系下的 ROI；由调用方按需进行缩放或映射
                    return cvRoi;
                }
                finally
                {
                    display?.Dispose();
                }
            }
            catch
            {
                try { Cv2.DestroyAllWindows(); } catch { }
                return new OpenCvSharp.Rect();
            }
        }

        /// <summary>
        /// 在给定的 Mat 上选择 ROI，绘制完成后列出 UIElem 枚举的所有键并弹出对话框让用户选择将映射后的矩形保存到哪个键中。
        /// 该方法会直接在传入的字典中添加或覆盖对应键的值（字典为引用类型，调用方将看到修改），
        /// 并在成功保存后将字典持久化为 JSON 文件。
        /// 注意：传入的 targetRects 必须为可变字典（例如 Dictionary&lt;UIElem, Rectangle&gt;），方法不会因为字典为空而返回，
        /// 而是允许向其添加新键（基于 UIElem 枚举）。
        /// </summary>
        /// <param name="src">用于选择 ROI 的 Mat</param>
        /// <param name="windowRect">窗口实际矩形，用于映射坐标</param>
        /// <param name="targetRects">可变目标字典，键为名称，值为矩形；方法会把映射后的矩形写回所选键</param>
        /// <param name="assignedKey">输出：被选中的键（若未保存则为 null）</param>
        /// <param name="windowName">SelectROI 窗口名</param>
        /// <param name="showCrosshair">是否显示十字线</param>
        /// <returns>如果用户选择并保存到目标字典返回 true，否则返回 false。</returns>
        /// <param name="saveFilePath">可选：保存目标字典的 JSON 文件路径。如果为 null 或空，方法将使用应用程序运行目录下的 "targetRects.json" 作为默认文件名并写入。</param>
        public static bool SelectAndAssignROI(Mat src, OpenCvSharp.Rect windowRect, IDictionary<UIElem, OpenCvSharp.Rect> targetRects, out UIElem? assignedKey, string windowName = "Select ROI", bool showCrosshair = true, string saveFilePath = null)
        {
            assignedKey = null;
            if (targetRects == null)
                return false;
            var selectedRoi = SelectAndScaleROI(src, windowRect, windowName, showCrosshair);
            if (selectedRoi.Width <= 0 || selectedRoi.Height <= 0)
                return false;

            // 在 UI 线程弹出 WPF 对话框让用户选择目标键
            try
            {
                bool? dialogResult = null;
                string selectedKeyStr = null;
                UIElem selectedKeyEnum = default;

                Action showDialogAction = () =>
                {
                    var window = new System.Windows.Window
                    {
                        Title = "请选择保存到的目标",
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        ResizeMode = ResizeMode.NoResize,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Background = System.Windows.Media.Brushes.White
                    };

                    var root = new StackPanel
                    {
                        Margin = new Thickness(12)
                    };

                    var label = new TextBlock
                    {
                        Text = "Save the selected ROI to which target?",
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    var combo = new ComboBox
                    {
                        Width = 336,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    // 将 UIElem 枚举的所有名称作为可选项展示（允许添加/覆盖传入字典中的对应项）
                    foreach (var name in System.Enum.GetNames(typeof(UIElem)))
                        combo.Items.Add(name);
                    if (combo.Items.Count > 0)
                        combo.SelectedIndex = 0;

                    var buttonPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };

                    var ok = new Button
                    {
                        Content = "OK",
                        Width = 75,
                        Margin = new Thickness(0, 0, 8, 0),
                        IsDefault = true
                    };
                    var cancel = new Button
                    {
                        Content = "Cancel",
                        Width = 75,
                        IsCancel = true
                    };

                    ok.Click += (_, __) =>
                    {
                        selectedKeyStr = combo.SelectedItem?.ToString();
                        if (!string.IsNullOrEmpty(selectedKeyStr))
                        {
                            window.DialogResult = true;
                            window.Close();
                        }
                    };
                    cancel.Click += (_, __) =>
                    {
                        window.DialogResult = false;
                        window.Close();
                    };

                    buttonPanel.Children.Add(ok);
                    buttonPanel.Children.Add(cancel);

                    root.Children.Add(label);
                    root.Children.Add(combo);
                    root.Children.Add(buttonPanel);
                    window.Content = root;

                    dialogResult = window.ShowDialog();
                };

                if (Application.Current != null)
                {
                    if (Application.Current.Dispatcher.CheckAccess())
                        showDialogAction();
                    else
                        Application.Current.Dispatcher.Invoke(showDialogAction);
                }
                else
                {
                    showDialogAction();
                }

                if (dialogResult == true && !string.IsNullOrEmpty(selectedKeyStr))
                {
                    // 尝试将选中的字符串解析回枚举键（忽略大小写）
                    if (System.Enum.TryParse<UIElem>(selectedKeyStr, true, out selectedKeyEnum))
                    {
                        // 将所选 ROI（src/截图坐标系）写回传入的字典（调用者会看到该修改，因为字典是引用类型）
                        targetRects[selectedKeyEnum] = selectedRoi;
                        assignedKey = selectedKeyEnum;

                        // 持久化到 JSON 文件：使用传入的路径或默认文件名
                        string path = saveFilePath;
                        if (string.IsNullOrEmpty(path))
                        {
                            try
                            {
                                var baseDir = System.AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
                                path = Path.Combine(baseDir, "targetRects.json");
                            }
                            catch
                            {
                                path = "targetRects.json";
                            }
                        }

                        try
                        {
                            SaveTargetRectsToJson(path, targetRects);
                        }
                        catch
                        {
                            // 忽略持久化错误（不影响内存中的字典设置），但方法仍视为成功
                        }

                        return true;
                    }
                }
            }
            catch
            {
                // 忽略 UI 错误，返回 false
            }

            return false;
        }

        #region 持久化（JSON）
        private struct RectDto
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }

            public RectDto(OpenCvSharp.Rect r)
            {
                X = r.X; Y = r.Y; Width = r.Width; Height = r.Height;
            }

            public OpenCvSharp.Rect ToCv() => new OpenCvSharp.Rect(X, Y, Width, Height);
        }

        /// <summary>
        /// 使用硬编码的默认值初始化目标矩形字典。
        /// 这些值对应 targetRects.json 文件中的内容。
        /// </summary>
        public static void InitializeDefaultTargetRects()
        {
            TargetRects.Clear();
            TargetRects[UIElem.选项] = new OpenCvSharp.Rect(33, 630, 285, 35);
            TargetRects[UIElem.前往制造商] = new OpenCvSharp.Rect(406, 704, 170, 26);
            TargetRects[UIElem.车辆框] = new OpenCvSharp.Rect(528, 156, 222, 165);
            TargetRects[UIElem.升级与调教] = new OpenCvSharp.Rect(51, 426, 263, 32);
            TargetRects[UIElem.车辆熟练度] = new OpenCvSharp.Rect(52, 613, 263, 37);
            TargetRects[UIElem.车库中当前车型] = new OpenCvSharp.Rect(109, 26, 250, 38);
            TargetRects[UIElem.大世界安娜] = new OpenCvSharp.Rect(70, 716, 69, 23);
            TargetRects[UIElem.加点界面熟练度点数] = new OpenCvSharp.Rect(436, 651, 90, 29);
            TargetRects[UIElem.重新开始] = new OpenCvSharp.Rect(158, 704, 90, 25);
            TargetRects[UIElem.收集簿] = new OpenCvSharp.Rect(53, 542, 176, 31);
            TargetRects[UIElem.车库品牌] = new OpenCvSharp.Rect(314, 115, 174, 25);
            TargetRects[UIElem.车库界面我的车辆按钮] = new OpenCvSharp.Rect(52, 384, 260, 35);
            TargetRects[UIElem.从车库移除车辆] = new OpenCvSharp.Rect(444, 471, 473, 35);
            TargetRects[UIElem.技术点数可用] = new OpenCvSharp.Rect(377, 545, 205, 35);
            TargetRects[UIElem.车辆卡片界面当前驾驶的车辆] = new OpenCvSharp.Rect(291, 154, 226, 169);
            TargetRects[UIElem.整页] = new OpenCvSharp.Rect(0, 0, 1360, 768);
        }

        /// <summary>
        /// 将目标矩形字典保存为 JSON 文件（覆盖现有文件）。
        /// 使用枚举 UIElem 作为键，会以枚举名（字符串）写入 JSON 文件。
        /// </summary>
        /// <param name="filePath">目标文件路径</param>
        /// <param name="targetRects">要保存的字典（键为 UIElem）</param>
        /// <returns>保存成功返回 true，失败返回 false。</returns>
        public static bool SaveTargetRectsToJson(string filePath, IDictionary<UIElem, OpenCvSharp.Rect> targetRects)
        {
            try
            {
                var map = new Dictionary<string, RectDto>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in targetRects)
                {
                    // 使用枚举名作为 JSON 键
                    map[kv.Key.ToString()] = new RectDto(kv.Value);
                }

                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(map, opts);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载目标矩形字典。
        /// JSON 文件中键为枚举名（字符串），该方法会尝试解析回 UIElem 枚举键。
        /// 如果文件不存在或加载失败，将使用硬编码的默认值。
        /// </summary>
        /// <param name="filePath">JSON 文件路径</param>
        public static void LoadTargetRectsFromJson(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    InitializeDefaultTargetRects();
                    return;
                }

                var json = File.ReadAllText(filePath);
                var map = JsonSerializer.Deserialize<Dictionary<string, RectDto>>(json);
                if (map == null)
                {
                    InitializeDefaultTargetRects();
                    return;
                }

                foreach (var kv in map)
                {
                    if (System.Enum.TryParse<UIElem>(kv.Key, true, out var keyEnum))
                    {
                        TargetRects[keyEnum] = kv.Value.ToCv();
                    }
                }
            }
            catch
            {
                ClsLogger.Log("Failed to load target rects from JSON. The file may be missing or corrupted. Using default values.");
                InitializeDefaultTargetRects();
            }
        }
        #endregion
    }
}
