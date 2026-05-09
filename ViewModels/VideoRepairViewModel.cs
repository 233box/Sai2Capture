using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Services;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 视频修复视图模型
    /// 对应 Python fix.py + select.py 的功能
    /// </summary>
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

        /// <summary>是否有正在进行的任务</summary>
        [ObservableProperty] private bool _isProcessing;

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
                CustomDialogService.ShowInfoDialog(
                    $"视频修复完成！\n\n输出文件：{outputPath}\n总帧数：{TotalFrames}\n已修复：{RemovedFrames} 帧",
                    "修复完成");
            });
        }

        /// <summary>
        /// 选择视频文件
        /// </summary>
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
                    _logService.AddLog($"视频修复：已加载视频 {dialog.FileName}");
                }
                else
                {
                    MessageBox.Show("无法打开视频文件，请确认文件格式正确。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 选择参考帧图片
        /// </summary>
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

        /// <summary>
        /// 选择输出路径
        /// </summary>
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
            {
                OutputPath = dialog.FileName;
            }
        }

        /// <summary>
        /// 开始修复
        /// </summary>
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
                MessageBox.Show("请先选择错误帧参考图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(OutputPath))
            {
                MessageBox.Show("请先设置输出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _repairService.SimilarityThreshold = SimilarityThreshold;
            _logService.AddLog($"视频修复：开始处理 - 视频：{VideoPath}，阈值：{SimilarityThreshold}");
            await _repairService.StartRepairAsync(OutputPath);
        }

        /// <summary>
        /// 取消修复
        /// </summary>
        [RelayCommand]
        private void CancelRepair()
        {
            _repairService.CancelRepair();
            _logService.AddLog("视频修复：已取消");
        }
    }
}
