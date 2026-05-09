using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.IO;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 视频修复服务 — 检测并替换与参考"坏帧"相似的帧
    /// 对应 Python fix.py 的 SSIM 比对逻辑
    /// </summary>
    public partial class VideoRepairService : ObservableObject, IDisposable
    {
        private CancellationTokenSource? _cts;
        private VideoCapture? _browseCapture;
        private bool _disposed;

        /// <summary>视频文件路径</summary>
        [ObservableProperty] private string _videoPath = string.Empty;

        /// <summary>参考帧（错误帧）路径</summary>
        [ObservableProperty] private string _referenceImagePath = string.Empty;

        /// <summary>视频宽度</summary>
        [ObservableProperty] private int _videoWidth;

        /// <summary>视频高度</summary>
        [ObservableProperty] private int _videoHeight;

        /// <summary>相似度阈值（0~1），超过此值视为错误帧</summary>
        [ObservableProperty] private double _similarityThreshold = 0.85;

        /// <summary>是否正在处理</summary>
        [ObservableProperty] private bool _isProcessing;

        /// <summary>进度百分比（0~100）</summary>
        [ObservableProperty] private double _progress;

        /// <summary>视频总帧数</summary>
        [ObservableProperty] private int _totalFrames;

        /// <summary>当前处理的帧序号</summary>
        [ObservableProperty] private int _currentFrame;

        /// <summary>已移除的帧数</summary>
        [ObservableProperty] private int _removedFrames;

        /// <summary>状态文本</summary>
        [ObservableProperty] private string _statusText = "就绪";

        /// <summary>修复完成事件</summary>
        public event Action<string>? RepairCompleted;
        /// <summary>进度更新事件（后台线程）</summary>
        public event Action? ProgressUpdated;

        /// <summary>
        /// 设置视频文件
        /// </summary>
        public bool SetVideo(string path)
        {
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".avi" && ext != ".mov" && ext != ".mkv") return false;

            using var cap = new VideoCapture(path);
            if (!cap.IsOpened()) return false;

            VideoPath = path;
            TotalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);
            VideoWidth = (int)cap.Get(VideoCaptureProperties.FrameWidth);
            VideoHeight = (int)cap.Get(VideoCaptureProperties.FrameHeight);
            StatusText = $"已加载视频：{Path.GetFileName(path)}（共 {TotalFrames} 帧）";
            return true;
        }

        /// <summary>
        /// 设置参考帧图片
        /// </summary>
        public bool SetReferenceImage(string path)
        {
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") return false;

            using var img = Cv2.ImRead(path, ImreadModes.Color);
            if (img.Empty()) return false;

            ReferenceImagePath = path;
            StatusText = $"已加载参考帧：{Path.GetFileName(path)}";
            return true;
        }

        /// <summary>
        /// 生成默认输出路径（视频同目录，_fixed 后缀）
        /// </summary>
        public string GetDefaultOutputPath()
        {
            if (string.IsNullOrEmpty(VideoPath)) return string.Empty;
            var dir = Path.GetDirectoryName(VideoPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(VideoPath);
            var ext = Path.GetExtension(VideoPath);
            return Path.Combine(dir, $"{name}_fixed{ext}");
        }

        /// <summary>
        /// 打开视频用于帧浏览（保持 VideoCapture 打开以支持快速 seek）
        /// </summary>
        public void OpenVideoForBrowsing()
        {
            CloseVideoBrowsing();
            if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath)) return;

            _browseCapture = new VideoCapture(VideoPath);
            if (!_browseCapture.IsOpened())
            {
                _browseCapture.Dispose();
                _browseCapture = null;
            }
        }

        /// <summary>
        /// 获取指定帧号的帧图像
        /// </summary>
        public Mat? GetFrame(int frameIndex)
        {
            if (_browseCapture == null || !_browseCapture.IsOpened()) return null;
            if (frameIndex < 0 || frameIndex >= TotalFrames) return null;

            _browseCapture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            var frame = new Mat();
            if (!_browseCapture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                return null;
            }
            return frame;
        }

        /// <summary>
        /// 将当前帧导出为 PNG 文件
        /// </summary>
        public void ExportFrame(int frameIndex, string outputPath)
        {
            using var frame = GetFrame(frameIndex);
            if (frame == null) return;
            Cv2.ImWrite(outputPath, frame);
        }

        /// <summary>
        /// 关闭帧浏览
        /// </summary>
        public void CloseVideoBrowsing()
        {
            _browseCapture?.Release();
            _browseCapture?.Dispose();
            _browseCapture = null;
        }

        /// <summary>
        /// 启动修复处理（异步）
        /// </summary>
        public async Task StartRepairAsync(string outputPath)
        {
            if (IsProcessing) return;
            if (string.IsNullOrEmpty(VideoPath) || string.IsNullOrEmpty(ReferenceImagePath))
            {
                StatusText = "请先选择视频文件和参考帧";
                return;
            }

            IsProcessing = true;
            Progress = 0;
            CurrentFrame = 0;
            RemovedFrames = 0;
            StatusText = "正在准备...";
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => ProcessVideo(outputPath, _cts.Token), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"处理失败：{ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 取消修复
        /// </summary>
        public void CancelRepair()
        {
            _cts?.Cancel();
        }

        private void ProcessVideo(string outputPath, CancellationToken ct)
        {
            // 读取并预处理参考帧
            using var refImg = Cv2.ImRead(ReferenceImagePath, ImreadModes.Color);
            if (refImg.Empty())
            {
                StatusText = "无法加载参考帧图片";
                return;
            }
            using var refGray = new Mat();
            Cv2.CvtColor(refImg, refGray, ColorConversionCodes.BGR2GRAY);
            using var refResized = new Mat();
            Cv2.Resize(refGray, refResized, new OpenCvSharp.Size(320, 240));

            // 打开视频
            using var cap = new VideoCapture(VideoPath);
            if (!cap.IsOpened())
            {
                StatusText = "无法打开视频文件";
                return;
            }

            int fps = (int)cap.Get(VideoCaptureProperties.Fps);
            int width = (int)cap.Get(VideoCaptureProperties.FrameWidth);
            int height = (int)cap.Get(VideoCaptureProperties.FrameHeight);
            int total = (int)cap.Get(VideoCaptureProperties.FrameCount);

            TotalFrames = total;
            StatusText = $"开始处理，共 {total} 帧...";

            // 创建输出
            using var writer = new VideoWriter(outputPath, FourCC.XVID, fps, new OpenCvSharp.Size(width, height));

            Mat? lastGoodFrame = null;
            int frameIdx = 0;

            using var frameGray = new Mat();
            using var frameResized = new Mat();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using var frame = new Mat();
                if (!cap.Read(frame) || frame.Empty()) break;

                // 预处理当前帧
                Cv2.CvtColor(frame, frameGray, ColorConversionCodes.BGR2GRAY);
                Cv2.Resize(frameGray, frameResized, new OpenCvSharp.Size(320, 240));

                // 计算结构相似度
                double similarity = ComputeSSIM(refResized, frameResized);

                if (similarity > SimilarityThreshold)
                {
                    // 错误帧，用上一帧替换
                    RemovedFrames++;
                    if (lastGoodFrame != null)
                        writer.Write(lastGoodFrame);
                    else
                        writer.Write(frame); // 第一帧即使是坏帧也写入
                }
                else
                {
                    writer.Write(frame);
                    // 复用上一帧的 Mat 避免每帧都 Clone
                    lastGoodFrame?.Dispose();
                    lastGoodFrame = frame.Clone();
                }

                frameIdx++;
                CurrentFrame = frameIdx;
                Progress = (double)frameIdx / total * 100;
                ProgressUpdated?.Invoke();
            }

            lastGoodFrame?.Dispose();
            writer.Release();

            StatusText = $"处理完成！共移除 {RemovedFrames} 帧";
            Progress = 100;
            ProgressUpdated?.Invoke();
            RepairCompleted?.Invoke(outputPath);
        }

        /// <summary>
        /// 计算两幅灰度图的结构相似度（SSIM）
        /// 匹配 skimage.metrics.structural_similarity 默认参数：
        /// uniform 窗口（7×7 box filter），K1=0.01, K2=0.03, L=255
        /// SSIM = (2*μx*μy + C1)*(2*σxy + C2) / ((μx² + μy² + C1)*(σx² + σy² + C2))
        /// </summary>
        private static double ComputeSSIM(Mat img1, Mat img2)
        {
            const double C1 = 6.5025;   // (0.01 * 255)²
            const double C2 = 58.5225;  // (0.03 * 255)²
            var winSize = new OpenCvSharp.Size(7, 7); // skimage 默认窗口

            using var f1 = new Mat();
            using var f2 = new Mat();
            img1.ConvertTo(f1, MatType.CV_32F);
            img2.ConvertTo(f2, MatType.CV_32F);

            // 均值（uniform box filter，非 Gaussian）
            using var mu1 = new Mat();
            using var mu2 = new Mat();
            Cv2.Blur(f1, mu1, winSize);
            Cv2.Blur(f2, mu2, winSize);

            // μx², μy², μx*μy
            using var mu1Sq = mu1.Mul(mu1);
            using var mu2Sq = mu2.Mul(mu2);
            using var mu1Mu2 = mu1.Mul(mu2);

            // f² 和 f*f
            using var f1Sq = f1.Mul(f1);
            using var f2Sq = f2.Mul(f2);
            using var f1f2 = f1.Mul(f2);

            // E[f²] 和 E[f1*f2]（uniform box filter）
            using var sigma1Sq = new Mat();
            using var sigma2Sq = new Mat();
            using var sigma12 = new Mat();
            Cv2.Blur(f1Sq, sigma1Sq, winSize);
            Cv2.Blur(f2Sq, sigma2Sq, winSize);
            Cv2.Blur(f1f2, sigma12, winSize);

            // 方差和协方差：σ² = E[f²] - (E[f])²
            Cv2.Subtract(sigma1Sq, mu1Sq, sigma1Sq);
            Cv2.Subtract(sigma2Sq, mu2Sq, sigma2Sq);
            Cv2.Subtract(sigma12, mu1Mu2, sigma12);

            // SSIM 公式：分子 = (2*μx*μy + C1) * (2*σxy + C2)
            using var t1 = new Mat();
            using var t2 = new Mat();
            Cv2.Add(mu1Mu2 * 2, C1, t1);
            Cv2.Add(sigma12 * 2, C2, t2);
            using var numerator = t1.Mul(t2);

            // 分母 = (μx² + μy² + C1) * (σx² + σy² + C2)
            Cv2.Add(mu1Sq, mu2Sq, t1);
            Cv2.Add(t1, C1, t1);
            Cv2.Add(sigma1Sq, sigma2Sq, t2);
            Cv2.Add(t2, C2, t2);
            using var denominator = t1.Mul(t2);

            using var ssimMap = new Mat();
            Cv2.Divide(numerator, denominator, ssimMap);

            return Cv2.Mean(ssimMap).Val0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _cts?.Cancel();
            _cts?.Dispose();
            CloseVideoBrowsing();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
