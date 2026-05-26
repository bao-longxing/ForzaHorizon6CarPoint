using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;

namespace FH_WPF
{
    internal static class ClsOCR
    {
        // 池的最大并发实例数，默认取逻辑核心数（上限 8）
        //private static readonly int PoolSize = Math.Min(Environment.ProcessorCount, 8);
        private static readonly int PoolSize = 3;

        private static readonly ConcurrentQueue<PaddleOcrAll> _pool = new();
        private static readonly SemaphoreSlim _semaphore = new(0);
        private static readonly object _initLock = new();
        private static FullOcrModel _defaultModel = LocalFullModels.ChineseV3;
        private static bool _initialized = false;

        /// <summary>预初始化 OCR 池（首次调用会创建全部实例，后续复用）</summary>
        public static void Initialize(FullOcrModel? model = null)
        {
            lock (_initLock)
            {
                EnsurePoolFilled(model);
            }
        }

        /// <summary>释放全部池中实例（程序退出时调用）</summary>
        public static void Dispose()
        {
            lock (_initLock)
            {
                _initialized = false;
                while (_pool.TryDequeue(out var instance))
                    instance.Dispose();
            }
        }

        /// <summary>从字节数组识别图片中的文本</summary>
        public static PaddleOcrResult RecognizeFromBytes(byte[] imageData)
        {
            ArgumentNullException.ThrowIfNull(imageData);

            lock (_initLock)
            {
                EnsurePoolFilled();
            }

            // 从池中借出一个实例
            _semaphore.Wait();
            if (!_pool.TryDequeue(out var ocr))
                throw new InvalidOperationException("OCR 池状态异常");

            try
            {
                return RecognizeOnce(ocr, imageData);
            }
            catch (Exception ex) when (IsDetectorRunFailed(ex))
            {
                Debug.WriteLine($"OCR detector failed, recreating predictor: {ex.Message}");
                ocr.Dispose();
                ocr = CreateInstance();
                return RecognizeOnce(ocr, imageData);
            }
            finally
            {
                // 归还实例到池
                _pool.Enqueue(ocr);
                _semaphore.Release();
            }
        }

        /// <summary>从 base64 字符串识别（适用于 GetSourceScreenshot 返回的数据）</summary>
        public static PaddleOcrResult RecognizeFromBase64(string base64Image)
        {
            if (string.IsNullOrEmpty(base64Image)) throw new ArgumentNullException(nameof(base64Image));
            var idx = base64Image.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                base64Image = base64Image[(idx + "base64,".Length)..];
            return RecognizeFromBytes(Convert.FromBase64String(base64Image));
        }

        /// <summary>下载远程图片并识别</summary>
        public static PaddleOcrResult RecognizeFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            using HttpClient http = new();
            byte[] data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            return RecognizeFromBytes(data);
        }

        private static void EnsurePoolFilled(FullOcrModel? model = null)
        {
            if (_initialized) return;
            _defaultModel = model ?? _defaultModel;
            for (int i = 0; i < PoolSize; i++)
            {
                _pool.Enqueue(CreateInstance());
            }
            _semaphore.Release(PoolSize);
            _initialized = true;
        }

        private static PaddleOcrAll CreateInstance()
        {
            PaddleConfig config = new PaddleConfig()
            {
                MkldnnEnabled = true,
                MkldnnCacheCapacity = 20,
                CpuMathThreadCount = 4
            };

            return new PaddleOcrAll(_defaultModel, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
        }

        private static PaddleOcrResult RecognizeOnce(PaddleOcrAll ocr, byte[] imageData)
        {
            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (src.Empty())
                throw new ArgumentException("无法解码 OCR 图片数据", nameof(imageData));
            return ocr.Run(src);
        }

        private static bool IsDetectorRunFailed(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                var message = current.Message ?? string.Empty;
                if (message.IndexOf("PaddlePredictor", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    message.IndexOf("run failed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
