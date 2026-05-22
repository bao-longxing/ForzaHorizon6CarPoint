using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FH_WPF
{
    internal static class ClsGameControl
    {
        #region 功能: 高级操作
        /// <summary>
        /// 进入游戏并发送回车键（高层操作）。
        /// </summary>
        public static void EnterTheGame()
        {
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
            if (!TryGetObsScreenshotMat(out Mat imageMat))
            {
                return;
            }

            using (imageMat)
            {
                if (debug)
                {
                    try { Cv2.ImShow("1.Game Screenshot - original", imageMat); } catch { }
                }

                OpenCvSharp.Rect optionBaseRect = ClsROI.TargetRects[ClsROI.UIElem.选项];
                var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                var targetSize = new OpenCvSharp.Size(imageMat.Width, imageMat.Height);
                OpenCvSharp.Rect scaledRect = ClsROI.ScaleFromBase(optionBaseRect, baseResolution, targetSize);

                if (!TryCreateSafeCropRect(imageMat, scaledRect, out OpenCvSharp.Rect cropRect))
                {
                    Debug.WriteLine("ClickOptionButton: 裁剪矩形超出图片范围或尺寸无效。");
                    if (debug) try { Cv2.ImShow("Game Screenshot - original", imageMat); } catch { }
                    return;
                }

                using Mat cropped = new Mat(imageMat, cropRect);

                if (debug)
                {
                    try { Cv2.ImShow("2.Cropped Screenshot - raw", cropped); } catch { }
                }

                if (!TryEncodeMatAsPng(cropped, out byte[] croppedBytes))
                {
                    return;
                }

                var ocrRst = ClsOCR.RecognizeFromBytes(croppedBytes);
                var region = ocrRst?.Regions?.FirstOrDefault(p => p.Text == "选项");
                if (region == null || (region?.Score == 0))
                {
                    Debug.WriteLine("ClickOptionButton: OCR did not find text '选项' in crop. Showing crop for debug.");
                    if (debug) try { Cv2.ImShow("Cropped Screenshot - raw", cropped); } catch { }
                    return;
                }

                RotatedRect optionRECT = region.Value.Rect;

                var centerPtCropped = new OpenCvSharp.Point((int)optionRECT.Center.X, (int)optionRECT.Center.Y);
                Cv2.Circle(cropped, centerPtCropped, 8, new Scalar(0, 0, 255), 2);
                Cv2.PutText(cropped, "Option", new OpenCvSharp.Point(centerPtCropped.X + 8, centerPtCropped.Y), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);

                if (debug)
                {
                    try { Cv2.ImShow("3.Cropped Screenshot - marked", cropped); } catch { }
                }

                FocusWindowByProcessName("forzahorizon6");
                if (!TryGetWindowRectByProcessName("forzahorizon6", out OpenCvSharp.Rect wndRect))
                {
                    Debug.WriteLine("ClickOptionButton: 未能获取游戏窗口位置。显示裁剪图以便调试。");
                    if (debug) try { Cv2.ImShow("Cropped Screenshot - marked", cropped); } catch { }
                    return;
                }

                Thread.Sleep(100);

                double originalCenterX = optionRECT.Center.X + cropRect.X;
                double originalCenterY = optionRECT.Center.Y + cropRect.Y;

                Debug.WriteLine($"ClickOptionButton: AOI scaled rect={scaledRect.X},{scaledRect.Y},{scaledRect.Width}x{scaledRect.Height} (base->{baseResolution.Width}x{baseResolution.Height} -> img={imageMat.Width}x{imageMat.Height})");
                Debug.WriteLine($"ClickOptionButton: OCR center(cropped)={optionRECT.Center.X:F1},{optionRECT.Center.Y:F1}, crop={cropRect.Width}x{cropRect.Height} @ {cropRect.X},{cropRect.Y}");
                if (!TryClickImagePoint(imageMat, originalCenterX, originalCenterY, "ClickOptionButton: 点击选项"))
                {
                    return;
                }

                ClsLogger.Log("已点击选项按钮");
            }
        }

        /// <summary>
        /// 在车辆界面执行删除车辆的点击动作（通过查找屏幕中“选项”文本定位）。
        /// 注意：方法中并未包含删除确认等后续流程，仅负责定位并点击“选项”位置，调用者应在需要时补充完整删除逻辑。
        /// </summary>
        public static void DeleteCar()
        {
            //判断当前界面是否在车库，如果不在车库则不执行删除操作
            if (!IsInGarage())
            {
                Debug.WriteLine("DeleteCar: 当前不在车库界面，取消删除操作。");
                return;
            }

        }

        public static void UpCarPoint(string manufacturerName, string modelName, bool IsDebug = false)
        {
            const int groupIntervalMs = 260;
            const int keyHoldMs = 80;
            const int inGroupGapMs = 1000;

            void DebugShow(Mat mat, string title)
            {
                if (!IsDebug) return;
                try
                {
                    using var d = mat.Clone();
                    Cv2.PutText(d, title, new OpenCvSharp.Point(8, 24), HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 255, 0), 2);
                    Cv2.ImShow(title, d);
                }
                catch { }
            }

            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var t = s.Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
                return t;
            }


            // 判断当前界面是否在车库，如果不在车库则不执行升级操作
            if (!IsInGarage())
            {
                ClsLogger.LogPoint("UpCarPoint: 当前不在车库界面，取消升级操作。");
                return;
            }
            ClsLogger.LogPoint($"步骤1-3: 开始点击前往制造商 ({manufacturerName})");

            if (!TryGetObsScreenshotMat(out Mat imageMat))
            {
                ClsLogger.LogPoint("UpCarPoint: 未能获取 OBS 截图。");
                return;
            }

            using (imageMat)
            {
                DebugShow(imageMat, "Step1-3 Original");
                if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.前往制造商, out OpenCvSharp.Rect targetBaseRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: ROI 中未配置 '前往制造商'。");
                    return;
                }

                var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                var targetSize = new OpenCvSharp.Size(imageMat.Width, imageMat.Height);
                OpenCvSharp.Rect scaledRect = ClsROI.ScaleFromBase(targetBaseRect, baseResolution, targetSize);

                if (!TryCreateSafeCropRect(imageMat, scaledRect, out OpenCvSharp.Rect cropRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: 前往制造商 ROI 裁剪无效。");
                    return;
                }

                using Mat cropped = new Mat(imageMat, cropRect);
                DebugShow(cropped, "Step1-3 Cropped - GoToManufacturer");
                if (!TryEncodeMatAsPng(cropped, out byte[] croppedBytes))
                {
                    ClsLogger.LogPoint("UpCarPoint: 前往制造商裁剪图编码失败。");
                    return;
                }

                var ocrRst = ClsOCR.RecognizeFromBytes(croppedBytes);
                var region = ocrRst?.Regions?.FirstOrDefault(p => (p.Text ?? string.Empty).IndexOf("前往制造商", StringComparison.OrdinalIgnoreCase) >= 0);
                if (region == null || region?.Score == 0)
                {
                    ClsLogger.LogPoint("UpCarPoint: 未识别到前往制造商。");
                    return;
                }
                double originalCenterX = region.Value.Rect.Center.X + cropRect.X;
                double originalCenterY = region.Value.Rect.Center.Y + cropRect.Y;
                Thread.Sleep(500);
                if (!TryClickImagePoint(imageMat, originalCenterX, originalCenterY, "点击前往制造商"))
                {
                    return;
                }

                ClsLogger.LogPoint("步骤1-3: 已点击前往制造商");
                return;
            }

            Thread.Sleep(800);

            // 4~6: 点击“斯巴鲁”，找不到则 PageUp 后重试一次
            ClsLogger.LogPoint($"步骤4-6: 开始查找并点击制造商 '{manufacturerName}'");
            bool clickedSubaru = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!TryGetObsScreenshotMat(out Mat manuMat))
                {
                    ClsLogger.LogPoint("UpCarPoint: 获取制造商界面截图失败。");
                    return;
                }

                using (manuMat)
                {
                    DebugShow(manuMat, $"Step4-6 Original (attempt {attempt + 1})");
                    if (!TryEncodeMatAsPng(manuMat, out byte[] manuBytes))
                    {
                        ClsLogger.LogPoint("UpCarPoint: 制造商界面截图编码失败。");
                        return;
                    }

                    var manuOcr = ClsOCR.RecognizeFromBytes(manuBytes);
                    var subaruRegion = manuOcr?.Regions?.FirstOrDefault(p =>
                        (p.Text ?? string.Empty).IndexOf(manufacturerName ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (subaruRegion != null && subaruRegion?.Score > 0)
                    {
                        // mark and show the found region when debugging
                        try
                        {
                            if (IsDebug)
                            {
                                using var mcopy = manuMat.Clone();
                                var c = new OpenCvSharp.Point((int)subaruRegion.Value.Rect.Center.X, (int)subaruRegion.Value.Rect.Center.Y);
                                Cv2.Circle(mcopy, c, 10, new Scalar(0, 0, 255), 3);
                                Cv2.PutText(mcopy, "Subaru", new OpenCvSharp.Point(c.X + 12, c.Y), HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 255, 0), 2);
                                Cv2.ImShow("Step4-6 Marked - Subaru", mcopy);
                            }
                        }
                        catch { }

                        if (!TryClickImagePoint(manuMat, subaruRegion.Value.Rect.Center.X, subaruRegion.Value.Rect.Center.Y, $"点击{manufacturerName}"))
                        {
                            return;
                        }

                        ClsLogger.LogPoint($"步骤4-6: 已点击{manufacturerName}");
                        clickedSubaru = true;
                        break;
                    }
                }

                if (attempt == 0)
                {
                    ClsLogger.LogPoint($"步骤5: 未找到{manufacturerName}，执行 PageUp 后重试");
                    FocusWindowByProcessName("forzahorizon6");
                    ClsLogicContorl_Ghub.ClickKey(Key.PageUp, keyHoldMs);
                    Thread.Sleep(500);
                }
            }

            if (!clickedSubaru)
            {
                ClsLogger.LogPoint($"UpCarPoint: 未找到{manufacturerName}（已重试 PageUp 一次）。");
                return;
            }

            return;


            Thread.Sleep(800);

            // 7~9: 查找车型，扩展 ROI，检测“全新”
            ClsLogger.LogPoint($"步骤7-9: 开始查找车型 '{modelName}' 与全新标记");
            if (!TryGetObsScreenshotMat(out Mat carFactoryMat))
            {
                ClsLogger.LogPoint("UpCarPoint: 获取车厂界面截图失败。");
                return;
            }

            using (carFactoryMat)
            {
                DebugShow(carFactoryMat, "Step7 Original - Factory");
                if (!TryEncodeMatAsPng(carFactoryMat, out byte[] factoryBytes))
                {
                    ClsLogger.LogPoint("UpCarPoint: 车厂界面截图编码失败。");
                    return;
                }

                var factoryOcr = ClsOCR.RecognizeFromBytes(factoryBytes);
                var normalizedModel = Normalize(modelName ?? string.Empty);

                // 过滤掉车库中当前车型所在的 ROI，避免将该区域的文本误识别为目标车型
                OpenCvSharp.Rect excludeScaledRect = new OpenCvSharp.Rect();
                if (ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车库中当前车型, out OpenCvSharp.Rect excludeBaseRect))
                {
                    excludeScaledRect = ClsROI.ScaleFromBase(excludeBaseRect, new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight), new OpenCvSharp.Size(carFactoryMat.Width, carFactoryMat.Height));
                }

                var modelRegion = factoryOcr?.Regions?.FirstOrDefault(p =>
                {
                    var t = Normalize(p.Text ?? string.Empty);
                    if (string.IsNullOrEmpty(normalizedModel) || !t.Contains(normalizedModel)) return false;

                    // 若 OCR 区域中心位于排除的 ROI 内，则忽略该区域
                    if (excludeScaledRect.Width > 0 && excludeScaledRect.Height > 0)
                    {
                        var cx = (int)Math.Round(p.Rect.Center.X);
                        var cy = (int)Math.Round(p.Rect.Center.Y);
                        if (cx >= excludeScaledRect.X && cy >= excludeScaledRect.Y && cx < excludeScaledRect.X + excludeScaledRect.Width && cy < excludeScaledRect.Y + excludeScaledRect.Height)
                            return false;
                    }

                    return true;
                });

                if (modelRegion == null || modelRegion?.Score == 0)
                {
                    ClsLogger.LogPoint($"UpCarPoint: 未找到车型 '{modelName}'。");
                    return;
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

                // 使用车辆框（safeCarFrame）的真实宽高，但将其位置移动到 OCR 检测到的车型 region 处。
                // 需求：字体的 region 应该位于车辆框的顶部居中位置，而不是车辆框正中。
                // 实现策略：横向以车型中心居中，纵向使车型 region 的中心位于车辆框顶部下方的一个小偏移（约占框高度的 10%）处。
                RotatedRect modelRect = modelRegion.Value.Rect;
                int modelCenterX = (int)Math.Round(modelRect.Center.X);
                int modelCenterY = (int)Math.Round(modelRect.Center.Y);

                int expW = safeCarFrame.Width;
                int expH = safeCarFrame.Height;

                // 横向居中：使车辆框中心 X 对齐车型中心 X
                int expX = modelCenterX - expW / 2;
                // 纵向顶部定位：将字体中心放置在车辆框顶部下方的约 10% 高度处
                int marginFromTop = (int)Math.Round(expH * 0.06);
                int expY = modelCenterY - marginFromTop;

                // 约束到图像范围内（如果超出则修正）
                if (expX < 0) expX = 0;
                if (expY < 0) expY = 0;
                if (expX + expW > carFactoryMat.Width) expX = Math.Max(0, carFactoryMat.Width - expW);
                if (expY + expH > carFactoryMat.Height) expY = Math.Max(0, carFactoryMat.Height - expH);

                // 兜底：如果调整后仍然出现无效尺寸，则回退到原始 safeCarFrame
                if (expW <= 0 || expH <= 0)
                {
                    expX = safeCarFrame.X;
                    expY = safeCarFrame.Y;
                    expW = safeCarFrame.Width;
                    expH = safeCarFrame.Height;
                }

                OpenCvSharp.Rect expandedRect = new OpenCvSharp.Rect(expX, expY, expW, expH);

                // debug: 标记车型中心和最终 expanded rect
                if (IsDebug)
                {
                    try
                    {
                        using var mark = carFactoryMat.Clone();
                        var mc = new OpenCvSharp.Point(modelCenterX, (int)Math.Round(modelRect.Center.Y));
                        Cv2.Circle(mark, mc, 8, new Scalar(0, 0, 255), 3);
                        Cv2.Rectangle(mark, new OpenCvSharp.Point(expandedRect.X, expandedRect.Y), new OpenCvSharp.Point(expandedRect.X + expandedRect.Width, expandedRect.Y + expandedRect.Height), new Scalar(255, 0, 0), 2);
                        Cv2.PutText(mark, "ModelCenter", new OpenCvSharp.Point(mc.X + 10, mc.Y), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
                        Cv2.ImShow("Step7 Marked - Model and Expanded", mark);
                    }
                    catch { }
                }

                if (!TryCreateSafeCropRect(carFactoryMat, expandedRect, out OpenCvSharp.Rect safeExpandedRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: 扩展后的车型检测 ROI 无效。");
                    return;
                }

                using Mat expandedCropped = new Mat(carFactoryMat, safeExpandedRect);
                DebugShow(expandedCropped, "Step8 Expanded ROI Crop");
                if (!TryEncodeMatAsPng(expandedCropped, out byte[] expandedBytes))
                {
                    ClsLogger.LogPoint("UpCarPoint: 扩展 ROI 编码失败。");
                    return;
                }
                var expandedOcr = ClsOCR.RecognizeFromBytes(expandedBytes);
                var brandNewRegion = expandedOcr?.Regions?.FirstOrDefault(p => p.Text.Contains("全新"));
                if (brandNewRegion == null /*|| brandNewRegion?.Score == 0*/)
                {
                    ClsLogger.LogPoint($"步骤9: 扩展 ROI 内未检测到 '全新' ({modelName})。");
                    return;
                }

                ClsLogger.LogPoint($"步骤9: 检测到 '全新' ({modelName})");
                DebugShow(expandedCropped, $"Step9 Show - {modelName} Contains BrandNew");

                // 10: 点击全新并按下回车，等待12秒后按下ESC
                double brandNewX = brandNewRegion.Value.Rect.Center.X + safeExpandedRect.X;
                double brandNewY = brandNewRegion.Value.Rect.Center.Y + safeExpandedRect.Y;
                if (!TryClickImagePoint(carFactoryMat, brandNewX, brandNewY, "点击全新"))
                {
                    return;
                }

                Thread.Sleep(500);
                ClsLogicContorl_Ghub.ClickKey(Key.Enter, keyHoldMs);
                Thread.Sleep(500);
                ClsLogicContorl_Ghub.ClickKey(Key.Enter, keyHoldMs);
                ClsLogger.LogPoint("步骤10: 已点击全新并按下回车，等待12秒");
                Thread.Sleep(12000);
                ClsLogicContorl_Ghub.ClickKey(Key.Escape, keyHoldMs);
                ClsLogger.LogPoint("步骤10: 已按下 ESC");
            }


            Thread.Sleep(1000);

            // 11: 使用ROI：升级与调教,缩放后点击该位置
            ClsLogger.LogPoint("步骤11: 点击 ROI-升级与调教");
            if (!TryGetObsScreenshotMat(out Mat upgradeMat))
            {
                ClsLogger.LogPoint("UpCarPoint: 步骤11截图失败。");
                return;
            }

            using (upgradeMat)
            {
                DebugShow(upgradeMat, "Step11 Original - UpgradeAndTuning");
                if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.升级与调教, out OpenCvSharp.Rect upgradeBaseRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: ROI 中未配置 '升级与调教'。");
                    return;
                }

                var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                var targetSize = new OpenCvSharp.Size(upgradeMat.Width, upgradeMat.Height);
                var scaled = ClsROI.ScaleFromBase(upgradeBaseRect, baseResolution, targetSize);
                if (!TryCreateSafeCropRect(upgradeMat, scaled, out OpenCvSharp.Rect safeRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: 步骤11 ROI 无效。");
                    return;
                }

                double cx = safeRect.X + safeRect.Width / 2.0;
                double cy = safeRect.Y + safeRect.Height / 2.0;
                if (IsDebug)
                {
                    using var tmp = new Mat(upgradeMat, safeRect);
                    DebugShow(tmp, "步骤11 ROI 标记 - 升级与调教");
                }

                if (!TryClickImagePoint(upgradeMat, cx, cy, "点击升级与调教"))
                {
                    return;
                }
            }

            Thread.Sleep(500);

            // 12: 使用ROI：车辆熟练度，缩放后点击该位置
            ClsLogger.LogPoint("步骤12: 点击 ROI-车辆熟练度");
            if (!TryGetObsScreenshotMat(out Mat skillMat))
            {
                ClsLogger.LogPoint("UpCarPoint: 步骤12截图失败。");
                return;
            }

            using (skillMat)
            {
                DebugShow(skillMat, "Step12 Original - VehicleSkill");
                if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.车辆熟练度, out OpenCvSharp.Rect skillBaseRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: ROI 中未配置 '车辆熟练度'。");
                    return;
                }

                var baseResolution = new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight);
                var targetSize = new OpenCvSharp.Size(skillMat.Width, skillMat.Height);
                var scaled = ClsROI.ScaleFromBase(skillBaseRect, baseResolution, targetSize);
                if (!TryCreateSafeCropRect(skillMat, scaled, out OpenCvSharp.Rect safeRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: 步骤12 ROI 无效。");
                    return;
                }

                double cx = safeRect.X + safeRect.Width / 2.0;
                double cy = safeRect.Y + safeRect.Height / 2.0;
                if (IsDebug)
                {
                    using var tmp = new Mat(skillMat, safeRect);
                    DebugShow(tmp, "Step12 ROI Marked - VehicleSkill");
                }
                if (!TryClickImagePoint(skillMat, cx, cy, "点击车辆熟练度"))
                {
                    return;
                }
            }

            Thread.Sleep(800);

            // 13: 采图，使用ROI：熟练度点数，裁剪后判断点数是否 >=30
            ClsLogger.LogPoint("步骤13: 识别熟练度点数");
            if (!TryGetObsScreenshotMat(out Mat pointMat))
            {
                ClsLogger.LogPoint("UpCarPoint: 步骤13截图失败。");
                return;
            }

            int currentPoint = 0;
            using (pointMat)
            {
                if (!ClsROI.TargetRects.TryGetValue(ClsROI.UIElem.熟练度点数, out OpenCvSharp.Rect pointBaseRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: ROI 中未配置 '熟练度点数'。");
                    return;
                }

                var scaled = ClsROI.ScaleFromBase(pointBaseRect, new OpenCvSharp.Size(ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight), new OpenCvSharp.Size(pointMat.Width, pointMat.Height));
                if (!TryCreateSafeCropRect(pointMat, scaled, out OpenCvSharp.Rect safeRect))
                {
                    ClsLogger.LogPoint("UpCarPoint: 步骤13 ROI 无效。");
                    return;
                }

                using Mat pointCrop = new Mat(pointMat, safeRect);
                DebugShow(pointCrop, "Step13 Cropped - SkillPoints");
                if (!TryEncodeMatAsPng(pointCrop, out byte[] pointBytes))
                {
                    ClsLogger.LogPoint("UpCarPoint: 步骤13裁剪编码失败。");
                    return;
                }

                var pointOcr = ClsOCR.RecognizeFromBytes(pointBytes);
                var values = new List<int>();
                foreach (var r in pointOcr.Regions)
                {
                    var ms = System.Text.RegularExpressions.Regex.Matches(r.Text ?? string.Empty, @"\d+");
                    foreach (System.Text.RegularExpressions.Match m in ms)
                    {
                        if (int.TryParse(m.Value, out int v)) values.Add(v);
                    }
                }

                currentPoint = values.Count > 0 ? values.Max() : 0;
                ClsLogger.LogPoint($"步骤13: 当前熟练度点数={currentPoint}");
            }

            if (currentPoint < 30)
            {
                ClsLogger.LogPoint("步骤13: 点数小于30，终止步骤14。");
                return;
            }

            // 14: 依次按键序列
            ClsLogger.LogPoint("步骤14: 开始执行按键序列");
            FocusWindowByProcessName("forzahorizon6");
            Key[] directions = new[] { Key.Right, Key.Up, Key.Up, Key.Up, Key.Up, Key.Left };
            foreach (var dir in directions)
            {
                ClsLogicContorl_Ghub.ClickKey(Key.Enter, keyHoldMs);
                Thread.Sleep(inGroupGapMs);
                ClsLogicContorl_Ghub.ClickKey(dir, keyHoldMs);
                Thread.Sleep(groupIntervalMs);
            }

            ClsLogicContorl_Ghub.ClickKey(Key.Enter, keyHoldMs);
            Thread.Sleep(groupIntervalMs);
            ClsLogicContorl_Ghub.ClickKey(Key.Escape, keyHoldMs);
            Thread.Sleep(inGroupGapMs);
            ClsLogicContorl_Ghub.ClickKey(Key.Escape, keyHoldMs);
            ClsLogger.LogPoint("步骤14: 按键序列执行完成");
        }

        /// <summary>
        /// 判定当前游戏画面是否处于车库界面。
        /// 通过 OCR 在 OBS 截图中查找关键文本（例如“我的车辆”、“前往制造商”等）来决定是否满足车库界面特征。
        /// </summary>
        /// <returns>如果当前界面被判断为车库返回 true，否则返回 false。</returns>
        /// <summary>
        /// 判断当前画面是否为车库界面（高层操作）。
        /// </summary>
        /// <returns>如果是车库界面返回 true，否则 false。</returns>
        public static bool IsInGarage()
        {
            // 1. 从 OBS 获取流截图
            var sources = ClsObs._obs.GetCurrentProgramScene();
            string? base64Image = ClsObs.GetSourceScreenshotAsync(sources, "png", ClsObs.ScreenshotWidth, ClsObs.ScreenshotHeight, 100).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(base64Image))
            {
                Debug.WriteLine("IsInGarage: 未能从 OBS 获取截图（base64 为空）。");
                return false;
            }
            byte[] gameShot = Convert.FromBase64String(base64Image);
            // 2. OCR 计算位置
            var ocrRst = ClsOCR.RecognizeFromBytes(gameShot);

            bool hasMyCar = false;
            bool hasBackSpace = false;
            bool hasCarCollection = false;

            foreach (var p in ocrRst.Regions)
            {
                if (p.Text.Contains("我的车辆")) hasMyCar = true;
                if (p.Text.Contains("前往制造商")) hasBackSpace = true;
                if (p.Text.Contains("车辆收藏")) hasCarCollection = true;
            }

            // 最终一并判断
            bool IsInGarge = hasMyCar && hasBackSpace && !hasCarCollection;

            return IsInGarge;
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
                ClsLogicContorl_Ghub.ClickMouse(1);
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
