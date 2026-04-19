using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using Sai2Capture.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using LogLevel = Sai2Capture.Services.LogLevel;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 视频修复视图模型
    /// 负责视频修复页面的数据绑定和命令处理
    /// </summary>
    public partial class VideoRepairViewModel : ObservableObject
    {
        private readonly VideoRepairService _videoRepairService;
        private readonly LogService _logService;

        [ObservableProperty]
        private string _videoPath = string.Empty;

        [ObservableProperty]
        private string _outputPath = string.Empty;

        [ObservableProperty]
        private double _similarityThreshold = 0.85;

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private bool _isFrameSelectorOpen;

        [ObservableProperty]
        private int _currentFrameIndex;

        [ObservableProperty]
        private int _totalFrames;

        [ObservableProperty]
        private int _removedFrames;

        [ObservableProperty]
        private string _status = "请选择视频文件";

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private ImageSource? _previewImage;

        [ObservableProperty]
        private ImageSource? _referenceFrameImage;

        [ObservableProperty]
        private string _progressText = "0 / 0 帧";

        [ObservableProperty]
        private string _similarityText = "相似度: -";

        [ObservableProperty]
        private string _frameTimeText = "00:00.000";

        [ObservableProperty]
        private bool _hasReferenceFrame;

        [ObservableProperty]
        private string _referenceFrameInfo = "未设置参考帧";

        public VideoRepairViewModel(VideoRepairService videoRepairService, LogService logService)
        {
            _videoRepairService = videoRepairService;
            _logService = logService;

            _videoRepairService.ProgressChanged += OnProgressChanged;
            _videoRepairService.LogMessage += OnLogMessage;
        }

        /// <summary>
        /// 选择视频文件
        /// </summary>
        [RelayCommand]
        private void SelectVideo()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择视频文件",
                Filter = "视频文件 (*.mp4;*.avi;*.mkv;*.mov)|*.mp4;*.avi;*.mkv;*.mov|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                VideoPath = dialog.FileName;
                _videoRepairService.SetVideoPath(VideoPath);
                OutputPath = _videoRepairService.OutputPath;

                TotalFrames = _videoRepairService.TotalFrames;
                HasReferenceFrame = false;
                ReferenceFrameImage = null;
                ReferenceFrameInfo = "未设置参考帧";
                CurrentFrameIndex = 0;

                OnPropertyChanged(nameof(OutputPath));
                AddLog($"已选择视频：{VideoPath}");
            }
        }

        /// <summary>
        /// 打开帧选择器
        /// </summary>
        [RelayCommand]
        private void OpenFrameSelector()
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                MessageBox.Show("请先选择视频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CurrentFrameIndex = 0;
            IsFrameSelectorOpen = true;
            UpdateFramePreview();
            AddLog("已打开帧选择器");
        }

        /// <summary>
        /// 关闭帧选择器
        /// </summary>
        [RelayCommand]
        private void CloseFrameSelector()
        {
            IsFrameSelectorOpen = false;
        }

        /// <summary>
        /// 设置当前帧为参考帧
        /// </summary>
        [RelayCommand]
        private void SetReferenceFrame()
        {
            if (string.IsNullOrEmpty(VideoPath))
                return;

            if (_videoRepairService.SetReferenceFrameFromVideo(CurrentFrameIndex))
            {
                HasReferenceFrame = true;
                var thumbnail = _videoRepairService.GetReferenceFrameThumbnail();
                if (thumbnail != null && !thumbnail.Empty())
                {
                    ReferenceFrameImage = MatToImageSource(thumbnail);
                    thumbnail.Dispose();
                }
                
                int fps = Math.Max(_videoRepairService.TotalFrames, 1);
                var time = TimeSpan.FromMilliseconds(CurrentFrameIndex * 1000.0 / fps);
                ReferenceFrameInfo = $"第{CurrentFrameIndex}帧 ({time:mm\\:ss})";
                IsFrameSelectorOpen = false;
                AddLog($"已将第{CurrentFrameIndex}帧设置为参考帧");
            }
        }

        /// <summary>
        /// 更新帧预览
        /// </summary>
        private void UpdateFramePreview()
        {
            if (string.IsNullOrEmpty(VideoPath))
                return;

            try
            {
                var thumbnail = _videoRepairService.GetFrameThumbnail(CurrentFrameIndex);
                if (thumbnail != null && !thumbnail.Empty())
                {
                    PreviewImage = MatToImageSource(thumbnail);
                    thumbnail.Dispose();
                }

                // 更新时间显示
                int fps = Math.Max(_videoRepairService.TotalFrames, 1);
                var time = TimeSpan.FromMilliseconds(CurrentFrameIndex * 1000.0 / fps);
                FrameTimeText = time.ToString(@"mm\:ss\.fff");
            }
            catch (Exception ex)
            {
                AddLog($"预览更新失败：{ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 跳转到指定帧
        /// </summary>
        [RelayCommand]
        private void GoToFrame(int frameIndex)
        {
            if (frameIndex < 0)
                frameIndex = 0;
            if (frameIndex >= TotalFrames)
                frameIndex = TotalFrames - 1;

            CurrentFrameIndex = frameIndex;
            UpdateFramePreview();
        }

        /// <summary>
        /// 跳转到视频开头
        /// </summary>
        [RelayCommand]
        private void GoToStart()
        {
            GoToFrameCommand.Execute(0);
        }

        /// <summary>
        /// 跳转到视频结尾
        /// </summary>
        [RelayCommand]
        private void GoToEnd()
        {
            GoToFrameCommand.Execute(TotalFrames - 1);
        }

        /// <summary>
        /// 前进一帧
        /// </summary>
        [RelayCommand]
        private void NextFrame()
        {
            if (CurrentFrameIndex < TotalFrames - 1)
            {
                CurrentFrameIndex++;
                UpdateFramePreview();
            }
        }

        /// <summary>
        /// 后退一帧
        /// </summary>
        [RelayCommand]
        private void PrevFrame()
        {
            if (CurrentFrameIndex > 0)
            {
                CurrentFrameIndex--;
                UpdateFramePreview();
            }
        }

        /// <summary>
        /// 前进 30 帧
        /// </summary>
        [RelayCommand]
        private void StepForward()
        {
            int newIndex = Math.Min(CurrentFrameIndex + 30, TotalFrames - 1);
            if (newIndex != CurrentFrameIndex)
            {
                CurrentFrameIndex = newIndex;
                UpdateFramePreview();
            }
        }

        /// <summary>
        /// 后退 30 帧
        /// </summary>
        [RelayCommand]
        private void StepBackward()
        {
            int newIndex = Math.Max(CurrentFrameIndex - 30, 0);
            if (newIndex != CurrentFrameIndex)
            {
                CurrentFrameIndex = newIndex;
                UpdateFramePreview();
            }
        }

        /// <summary>
        /// 开始视频修复
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartRepair))]
        private async Task StartRepairAsync()
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                MessageBox.Show("请先选择视频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!HasReferenceFrame)
            {
                MessageBox.Show("请先从视频中选择一帧作为参考", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _videoRepairService.SimilarityThreshold = SimilarityThreshold;
            IsProcessing = true;
            AddLog($"开始视频修复 - 阈值：{SimilarityThreshold:F2}");

            try
            {
                await _videoRepairService.StartRepairAsync();
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanStartRepair()
        {
            return !string.IsNullOrEmpty(VideoPath) && 
                   HasReferenceFrame && 
                   !IsProcessing;
        }

        /// <summary>
        /// 取消处理
        /// </summary>
        [RelayCommand]
        private void CancelRepair()
        {
            _videoRepairService.CancelProcessing();
            AddLog("已请求取消处理");
        }

        /// <summary>
        /// 打开输出文件夹
        /// </summary>
        [RelayCommand]
        private void OpenOutputFolder()
        {
            if (!string.IsNullOrEmpty(OutputPath) && File.Exists(OutputPath))
            {
                string? folder = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
                }
            }
            else if (!string.IsNullOrEmpty(OutputPath))
            {
                string? folder = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
        }

        private void OnProgressChanged(object? sender, VideoRepairProgressEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CurrentFrameIndex = e.CurrentFrame;
                TotalFrames = e.TotalFrames;
                RemovedFrames = e.RemovedFrames;
                Progress = (double)e.CurrentFrame / Math.Max(e.TotalFrames, 1) * 100;
                Status = e.Status;
                ProgressText = $"{e.CurrentFrame} / {e.TotalFrames} 帧 (已移除 {e.RemovedFrames} 帧)";
                SimilarityText = $"相似度: {e.Similarity:F3}";

                // 更新预览图像
                if (_videoRepairService.CurrentPreviewFrame != null && !_videoRepairService.CurrentPreviewFrame.Empty())
                {
                    PreviewImage = MatToImageSource(_videoRepairService.CurrentPreviewFrame);
                }
            });
        }

        private void OnLogMessage(object? sender, string e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                AddLog(e);
            });
        }

        private void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            _logService.AddLog($"[视频修复] {message}", level, "VideoRepair");
        }

        /// <summary>
        /// 将 OpenCV Mat 转换为 WPF ImageSource
        /// </summary>
        private static ImageSource? MatToImageSource(Mat mat)
        {
            if (mat.Empty() || mat.Channels() != 3)
                return null;

            try
            {
                var width = mat.Width;
                var height = mat.Height;
                var stride = width * 3;
                var pixelData = new byte[height * stride];

                Marshal.Copy(mat.Data, pixelData, 0, pixelData.Length);

                return BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null,
                    pixelData,
                    stride);
            }
            catch
            {
                return null;
            }
        }

        partial void OnSimilarityThresholdChanged(double value)
        {
            if (_videoRepairService != null)
            {
                _videoRepairService.SimilarityThreshold = value;
            }
        }

        partial void OnCurrentFrameIndexChanged(int value)
        {
            if (!IsProcessing && !IsFrameSelectorOpen)
            {
                UpdateFramePreview();
            }
        }

        partial void OnIsFrameSelectorOpenChanged(bool value)
        {
            if (value)
            {
                UpdateFramePreview();
            }
        }

        public void Cleanup()
        {
            _videoRepairService.ProgressChanged -= OnProgressChanged;
            _videoRepairService.LogMessage -= OnLogMessage;
            _videoRepairService.Cleanup();
        }
    }
}
