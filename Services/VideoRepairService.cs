using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 视频修复进度事件参数
    /// </summary>
    public class VideoRepairProgressEventArgs : EventArgs
    {
        public int CurrentFrame { get; set; }
        public int TotalFrames { get; set; }
        public int RemovedFrames { get; set; }
        public double Similarity { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsProcessing { get; set; }
    }

    /// <summary>
    /// 视频信息
    /// </summary>
    public class VideoInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Fps { get; set; }
        public int TotalFrames { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// 视频修复服务 - 提供视频错误帧检测和修复功能
    /// 基于结构相似性（SSIM）算法检测并移除与参考帧高度相似的错误帧
    /// </summary>
    public partial class VideoRepairService : ObservableObject
    {
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private string _videoPath = string.Empty;

        [ObservableProperty]
        private string _outputPath = string.Empty;

        [ObservableProperty]
        private double _similarityThreshold = 0.85;

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private int _currentFrame;

        [ObservableProperty]
        private int _totalFrames;

        [ObservableProperty]
        private int _removedFrames;

        [ObservableProperty]
        private string _status = "就绪";

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private Mat? _currentPreviewFrame;

        // 内部状态
        private Mat? _referenceFrame;
        private VideoInfo? _videoInfo;

        public event EventHandler<VideoRepairProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// 获取视频信息
        /// </summary>
        public VideoInfo? GetVideoInfo(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                using var cap = new VideoCapture(path);
                if (!cap.IsOpened())
                    return null;

                int fps = (int)cap.Get(VideoCaptureProperties.Fps);
                int totalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);
                int width = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                int height = (int)cap.Get(VideoCaptureProperties.FrameHeight);

                _videoInfo = new VideoInfo
                {
                    Width = width,
                    Height = height,
                    Fps = fps,
                    TotalFrames = totalFrames,
                    Duration = TimeSpan.FromSeconds(totalFrames / (double)Math.Max(fps, 1))
                };

                return _videoInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 设置待处理的视频文件路径
        /// </summary>
        public void SetVideoPath(string path)
        {
            VideoPath = path;
            if (!string.IsNullOrEmpty(path))
            {
                OutputPath = Path.Combine(
                    Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory,
                    Path.GetFileNameWithoutExtension(path) + "_fixed.mp4");

                // 获取视频信息
                var info = GetVideoInfo(path);
                if (info != null)
                {
                    TotalFrames = info.TotalFrames;
                    OnLogMessage($"已加载视频：{info.Width}x{info.Height}, {info.Fps}FPS, 共{info.TotalFrames}帧");
                }
            }
        }

        /// <summary>
        /// 从视频中提取指定帧作为参考帧
        /// </summary>
        public bool SetReferenceFrameFromVideo(int frameIndex)
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                OnLogMessage("请先加载视频文件");
                return false;
            }

            try
            {
                using var cap = new VideoCapture(VideoPath);
                if (!cap.IsOpened())
                {
                    OnLogMessage("无法打开视频文件");
                    return false;
                }

                int totalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);
                if (frameIndex < 0 || frameIndex >= totalFrames)
                {
                    OnLogMessage($"帧索引无效：{frameIndex} (有效范围: 0-{totalFrames - 1})");
                    return false;
                }

                cap.Set(VideoCaptureProperties.PosFrames, frameIndex);
                using var frame = new Mat();
                if (!cap.Read(frame) || frame.Empty())
                {
                    OnLogMessage($"无法读取第{frameIndex}帧");
                    return false;
                }

                // 释放旧参考帧
                _referenceFrame?.Dispose();
                _referenceFrame = frame.Clone();

                var time = TimeSpan.FromSeconds(frameIndex / (double)Math.Max(cap.Get(VideoCaptureProperties.Fps), 1));
                OnLogMessage($"已设置第{frameIndex}帧为参考帧 (时间: {time:mm\\:ss\\.fff})");

                return true;
            }
            catch (Exception ex)
            {
                OnLogMessage($"设置参考帧失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取指定帧的缩略图
        /// </summary>
        public Mat? GetFrameThumbnail(int frameIndex)
        {
            if (string.IsNullOrEmpty(VideoPath))
                return null;

            try
            {
                using var cap = new VideoCapture(VideoPath);
                if (!cap.IsOpened())
                    return null;

                int totalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);
                if (frameIndex < 0 || frameIndex >= totalFrames)
                    return null;

                cap.Set(VideoCaptureProperties.PosFrames, frameIndex);
                using var frame = new Mat();
                if (!cap.Read(frame) || frame.Empty())
                    return null;

                // 缩放并转换为 RGB
                using var resized = new Mat();
                Cv2.Resize(frame, resized, new OpenCvSharp.Size(320, 240));
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
                return rgb.Clone();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查是否已设置参考帧
        /// </summary>
        public bool HasReferenceFrame => _referenceFrame != null && !_referenceFrame.Empty();

        /// <summary>
        /// 获取参考帧的缩略图
        /// </summary>
        public Mat? GetReferenceFrameThumbnail()
        {
            if (_referenceFrame == null || _referenceFrame.Empty())
                return null;

            try
            {
                using var resized = new Mat();
                Cv2.Resize(_referenceFrame, resized, new OpenCvSharp.Size(320, 240));
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
                return rgb.Clone();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 开始视频修复处理
        /// </summary>
        public async Task StartRepairAsync()
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                OnLogMessage("请先选择视频文件");
                return;
            }

            if (!HasReferenceFrame)
            {
                OnLogMessage("请先从视频中选择一帧作为参考");
                return;
            }

            if (!File.Exists(VideoPath))
            {
                OnLogMessage($"视频文件不存在：{VideoPath}");
                return;
            }

            IsProcessing = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await Task.Run(() => ProcessVideo(_cancellationTokenSource.Token));
            }
            catch (OperationCanceledException)
            {
                OnLogMessage("操作已取消");
                Status = "已取消";
            }
            catch (Exception ex)
            {
                OnLogMessage($"处理失败：{ex.Message}");
                Status = "处理失败";
            }
            finally
            {
                IsProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 取消正在进行的处理
        /// </summary>
        public void CancelProcessing()
        {
            _cancellationTokenSource?.Cancel();
        }

        private void ProcessVideo(CancellationToken token)
        {
            using var cap = new VideoCapture(VideoPath);
            if (!cap.IsOpened())
            {
                OnLogMessage("无法打开视频文件");
                return;
            }

            TotalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);
            int fps = (int)cap.Get(VideoCaptureProperties.Fps);
            int width = (int)cap.Get(VideoCaptureProperties.FrameWidth);
            int height = (int)cap.Get(VideoCaptureProperties.FrameHeight);

            OnLogMessage($"视频信息：{width}x{height}, {fps}FPS, 共{TotalFrames}帧");

            // 预处理参考帧
            using var refGray = new Mat();
            Cv2.CvtColor(_referenceFrame!, refGray, ColorConversionCodes.BGR2GRAY);
            using var refResized = new Mat();
            Cv2.Resize(refGray, refResized, new OpenCvSharp.Size(320, 240));

            // 创建输出视频
            using var writer = new VideoWriter(
                OutputPath,
                FourCC.MP4V,
                fps,
                new OpenCvSharp.Size(width, height));

            if (!writer.IsOpened())
            {
                OnLogMessage("无法创建输出视频文件");
                return;
            }

            CurrentFrame = 0;
            RemovedFrames = 0;

            using var lastValidFrame = new Mat();
            using var grayFrame = new Mat();
            using var resizedFrame = new Mat();

            Status = "正在处理...";
            OnLogMessage($"开始处理，相似度阈值：{SimilarityThreshold}");

            while (cap.IsOpened())
            {
                token.ThrowIfCancellationRequested();

                using var frame = new Mat();
                if (!cap.Read(frame) || frame.Empty())
                    break;

                CurrentFrame++;
                Progress = (double)CurrentFrame / TotalFrames * 100;

                // 预处理当前帧
                Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                Cv2.Resize(grayFrame, resizedFrame, new OpenCvSharp.Size(320, 240));

                // 计算相似度
                double similarity = CalculateSimilarity(refResized, resizedFrame);

                if (similarity > SimilarityThreshold)
                {
                    RemovedFrames++;
                    Status = $"跳过第{CurrentFrame}帧 (相似度: {similarity:F3})";

                    if (!lastValidFrame.Empty())
                    {
                        writer.Write(lastValidFrame);
                    }
                    else
                    {
                        writer.Write(frame);
                    }
                }
                else
                {
                    Status = $"保留第{CurrentFrame}帧 (相似度: {similarity:F3})";
                    writer.Write(frame);
                    frame.CopyTo(lastValidFrame);
                }

                // 每10帧更新一次预览
                if (CurrentFrame % 10 == 0)
                {
                    UpdatePreviewFrame(frame);
                }

                // 触发进度更新
                OnProgressChanged(new VideoRepairProgressEventArgs
                {
                    CurrentFrame = CurrentFrame,
                    TotalFrames = TotalFrames,
                    RemovedFrames = RemovedFrames,
                    Similarity = similarity,
                    Status = Status,
                    IsProcessing = true
                });
            }

            Status = "处理完成";
            OnLogMessage($"处理完成！共移除{RemovedFrames}帧，输出文件：{OutputPath}");

            OnProgressChanged(new VideoRepairProgressEventArgs
            {
                CurrentFrame = TotalFrames,
                TotalFrames = TotalFrames,
                RemovedFrames = RemovedFrames,
                IsProcessing = false
            });
        }

        /// <summary>
        /// 计算两幅灰度图像的相似度
        /// </summary>
        private static double CalculateSimilarity(Mat img1, Mat img2)
        {
            if (img1.Empty() || img2.Empty() || img1.Size() != img2.Size())
                return 0;

            using var diff = new Mat();
            Cv2.Absdiff(img1, img2, diff);
            Scalar mse = Cv2.Mean(diff);

            double maxDiff = 255.0;
            double similarity = 1.0 - (mse.Val0 / maxDiff);

            return Math.Clamp(similarity, 0, 1);
        }

        private void UpdatePreviewFrame(Mat frame)
        {
            try
            {
                if (frame == null || frame.Empty()) return;

                using var resized = new Mat();
                Cv2.Resize(frame, resized, new OpenCvSharp.Size(320, 240));
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

                CurrentPreviewFrame?.Dispose();
                CurrentPreviewFrame = rgb.Clone();
            }
            catch
            {
                // 预览更新失败不影响主流程
            }
        }

        private void OnProgressChanged(VideoRepairProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        private void OnLogMessage(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        public void Cleanup()
        {
            _referenceFrame?.Dispose();
            _referenceFrame = null;
            CurrentPreviewFrame?.Dispose();
            CurrentPreviewFrame = null;
        }
    }
}
