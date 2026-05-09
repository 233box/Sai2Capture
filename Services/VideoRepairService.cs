using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.IO;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 视频修复服务 — 检测并替换与参考"坏帧"相似的帧
    /// </summary>
    public partial class VideoRepairService : ObservableObject, IDisposable
    {
        private CancellationTokenSource? _cts;
        private VideoCapture? _browseCapture;
        private bool _disposed;

        [ObservableProperty] private string _videoPath = string.Empty;
        [ObservableProperty] private string _referenceImagePath = string.Empty;
        [ObservableProperty] private int _videoWidth;
        [ObservableProperty] private int _videoHeight;
        [ObservableProperty] private double _similarityThreshold = 0.85;
        [ObservableProperty] private bool _isProcessing;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private int _totalFrames;
        [ObservableProperty] private int _currentFrame;
        [ObservableProperty] private int _removedFrames;
        [ObservableProperty] private string _statusText = "就绪";

        public event Action<string>? RepairCompleted;
        public event Action? ProgressUpdated;

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

        public string GetDefaultOutputPath()
        {
            if (string.IsNullOrEmpty(VideoPath)) return string.Empty;
            var dir = Path.GetDirectoryName(VideoPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(VideoPath);
            var ext = Path.GetExtension(VideoPath);
            return Path.Combine(dir, $"{name}_fixed{ext}");
        }

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

        public void ExportFrame(int frameIndex, string outputPath)
        {
            using var frame = GetFrame(frameIndex);
            if (frame == null) return;
            Cv2.ImWrite(outputPath, frame);
        }

        public void CloseVideoBrowsing()
        {
            _browseCapture?.Release();
            _browseCapture?.Dispose();
            _browseCapture = null;
        }

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

        public void CancelRepair() => _cts?.Cancel();

        private void ProcessVideo(string outputPath, CancellationToken ct)
        {
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

                Cv2.CvtColor(frame, frameGray, ColorConversionCodes.BGR2GRAY);
                Cv2.Resize(frameGray, frameResized, new OpenCvSharp.Size(320, 240));

                double similarity = ComputeSimilarity(refResized, frameResized);

                if (similarity > SimilarityThreshold)
                {
                    RemovedFrames++;
                    if (lastGoodFrame != null)
                        writer.Write(lastGoodFrame);
                    else
                        writer.Write(frame);
                }
                else
                {
                    writer.Write(frame);
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
        /// 计算两幅灰度图的相似度
        /// 使用 OpenCV MatchTemplate 归一化相关系数（CCorrNormed）
        /// 返回值 0~1，1 表示完全相同
        /// </summary>
        private static double ComputeSimilarity(Mat img1, Mat img2)
        {
            using var result = new Mat();
            Cv2.MatchTemplate(img1, img2, result, TemplateMatchModes.CCorrNormed);
            var score = result.At<float>(0, 0);
            return Math.Max(0, score);
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
