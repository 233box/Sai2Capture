using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using Sai2Capture.src.Services;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;
using WinLogLevel = Sai2Capture.src.Services.LogLevel;

namespace Sai2Capture.src.ViewModels
{
    public partial class VideoRepairViewModel : ObservableObject
    {
        private readonly VideoRepairService _repairService;
        private readonly LogService _logService;

        [ObservableProperty] private string _videoPath = string.Empty;
        [ObservableProperty] private string _referenceImagePath = string.Empty;
        [ObservableProperty] private string _outputPath = string.Empty;
        [ObservableProperty] private double _similarityThreshold = 0.85;
        [ObservableProperty] private string _statusText = "就绪";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private int _totalFrames;
        [ObservableProperty] private int _currentFrame;
        [ObservableProperty] private int _removedFrames;
        [ObservableProperty] private bool _isProcessing;

        [ObservableProperty] private int _framePosition;
        [ObservableProperty] private BitmapSource? _previewImage;
        [ObservableProperty] private bool _isVideoLoaded;
        [ObservableProperty] private int _maxFramePosition;

        partial void OnFramePositionChanged(int value)
        {
            if (IsVideoLoaded && !IsProcessing)
                LoadPreviewFrame(value);
        }

        public VideoRepairViewModel(VideoRepairService repairService, LogService logService)
        {
            _repairService = repairService;
            _logService = logService;

            _repairService.ProgressUpdated += () =>
                WpfApplication.Current.Dispatcher.Invoke(RefreshFromService);
            _repairService.RepairCompleted += OnRepairCompleted;
        }

        private void RefreshFromService()
        {
            Progress = _repairService.Progress;
            CurrentFrame = _repairService.CurrentFrame;
            RemovedFrames = _repairService.RemovedFrames;
            TotalFrames = _repairService.TotalFrames;
            StatusText = _repairService.StatusText;
            IsProcessing = _repairService.IsProcessing;
        }

        private void OnRepairCompleted(string outputPath)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                RefreshFromService();
                IsProcessing = false;
                CustomDialogService.ShowInfoDialog(
                    $"视频修复完成！\n\n输出文件：{outputPath}\n总帧数：{TotalFrames}\n已修复：{RemovedFrames} 帧",
                    "修复完成");
            });
        }

        [RelayCommand]
        private void SelectVideo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择视频文件",
                Filter = "视频文件 (*.mp4;*.avi;*.mov;*.mkv)|*.mp4;*.avi;*.mov;*.mkv|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_repairService.SetVideo(dialog.FileName))
                {
                    VideoPath = dialog.FileName;
                    OutputPath = _repairService.GetDefaultOutputPath();
                    ReferenceImagePath = string.Empty;
                    _logService.AddLog($"视频修复：已加载视频 {dialog.FileName}");
                    StartFrameBrowsing();
                }
                else
                {
                    MessageBox.Show("无法打开视频文件，请确认文件格式正确。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void SelectReferenceImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择错误帧参考图片",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_repairService.SetReferenceImage(dialog.FileName))
                {
                    ReferenceImagePath = dialog.FileName;
                    _logService.AddLog($"视频修复：已加载参考帧 {dialog.FileName}");
                }
                else
                {
                    MessageBox.Show("无法加载参考图片，请确认文件格式正确。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void SetCurrentFrameAsReference()
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Sai2Capture");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, $"ref_frame_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                _repairService.ExportFrame(FramePosition, tempFile);

                if (File.Exists(tempFile))
                {
                    _repairService.SetReferenceImage(tempFile);
                    ReferenceImagePath = tempFile;
                    StatusText = $"已将第 {FramePosition} 帧设为错误帧参考图";
                    _logService.AddLog($"视频修复：从视频中提取第 {FramePosition} 帧作为参考图 → {tempFile}");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"提取参考帧失败：{ex.Message}", WinLogLevel.Error);
                MessageBox.Show($"提取参考帧失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SelectOutputPath()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存修复后的视频",
                Filter = "MP4 视频 (*.mp4)|*.mp4|所有文件 (*.*)|*.*",
                FileName = OutputPath
            };

            if (dialog.ShowDialog() == true)
                OutputPath = dialog.FileName;
        }

        [RelayCommand]
        private async Task StartRepair()
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                MessageBox.Show("请先选择视频文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(ReferenceImagePath))
            {
                MessageBox.Show("请先设置错误帧参考图。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(OutputPath))
            {
                MessageBox.Show("请先设置输出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _repairService.CloseVideoBrowsing();
            _repairService.SimilarityThreshold = SimilarityThreshold;
            _logService.AddLog($"视频修复：开始处理 - 视频：{VideoPath}，阈值：{SimilarityThreshold}");
            await _repairService.StartRepairAsync(OutputPath);
        }

        [RelayCommand]
        private void CancelRepair()
        {
            _repairService.CancelRepair();
            _logService.AddLog("视频修复：已取消");
        }

        private void StartFrameBrowsing()
        {
            _repairService.OpenVideoForBrowsing();
            TotalFrames = _repairService.TotalFrames;
            MaxFramePosition = Math.Max(0, TotalFrames - 1);
            FramePosition = 0;
            IsVideoLoaded = true;
            StatusText = $"已加载视频：{Path.GetFileName(VideoPath)}（共 {TotalFrames} 帧）";
        }

        private void LoadPreviewFrame(int frameIndex)
        {
            try
            {
                using var frame = _repairService.GetFrame(frameIndex);
                if (frame == null || frame.Empty()) return;
                PreviewImage = UtilityService.MatToBitmapSource(frame);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载预览帧 {frameIndex} 失败：{ex.Message}", WinLogLevel.Warning);
            }
        }
    }
}
