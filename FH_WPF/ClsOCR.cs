using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using System;
using System.Diagnostics;
using System.Net.Http;

namespace FH_WPF
{
    internal static class ClsOCR
    {
        private static readonly object _initLock = new object();
        private static PaddleOcrAll? _ocrAll = null;
        private static FullOcrModel _defaultModel = LocalFullModels.ChineseV3;

        // 初始化 OCR（首次调用会创建实例，后续复用）
        public static void Initialize(FullOcrModel? model = null)
        {
            if (_ocrAll != null) return;
            lock (_initLock)
            {
                if (_ocrAll != null) return;
                var useModel = model ?? _defaultModel;
                _ocrAll = new PaddleOcrAll(useModel, PaddleDevice.Mkldnn())
                {
                    AllowRotateDetection = false,
                    Enable180Classification = false,
                };
            }
        }

        // 释放资源（程序退出时可调用）
        public static void Dispose()
        {
            lock (_initLock)
            {
                if (_ocrAll != null)
                {
                    _ocrAll.Dispose();
                    _ocrAll = null;
                }
            }
        }

        // 从字节数组识别图片中的文本
        public static PaddleOcrResult RecognizeFromBytes(byte[] imageData)
        {
            if (imageData == null) throw new ArgumentNullException(nameof(imageData));
            Initialize();
            if (_ocrAll == null) throw new InvalidOperationException("OCR 未初始化");

            using (var src = Cv2.ImDecode(imageData, ImreadModes.Color))
            {
                return _ocrAll.Run(src);
            }
        }

        // 从 base64 字符串识别（适用于 GetSourceScreenshot 返回的数据）
        public static PaddleOcrResult RecognizeFromBase64(string base64Image)
        {
            if (string.IsNullOrEmpty(base64Image)) throw new ArgumentNullException(nameof(base64Image));
            var idx = base64Image.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                base64Image = base64Image.Substring(idx + "base64,".Length);
            }
            var bytes = Convert.FromBase64String(base64Image);
            return RecognizeFromBytes(bytes);
        }

        // 示例：下载远程图片并识别（保留以便测试）
        public static PaddleOcrResult RecognizeFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            using (HttpClient http = new HttpClient())
            {
                byte[] data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                return RecognizeFromBytes(data);
            }
        }
    }
}
