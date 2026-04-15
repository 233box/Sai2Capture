using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Models;
using Sai2Capture.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 视频文件信息（用于 UI 显示）
    /// </summary>
    public class VideoFileInfo : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public double FileSizeKB { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public string SizeDisplay => FileSizeKB > 1024 * 1024
            ? $"{FileSizeKB / 1024 / 1024:F2} GB"
            : FileSizeKB > 1024
                ? $"{FileSizeKB / 1024:F2} MB"
                : $"{FileSizeKB:F2} KB";
        public string ResolutionDisplay => CanvasWidth > 0 && CanvasHeight > 0
            ? $"{CanvasWidth} x {CanvasHeight}"
            : "未知";
    }

    /// <summary>
    /// 录制文件管理视图模型（回退到直接 MP4 文件管理）
    /// </summary>
    public partial class RecordingManagerViewModel : ObservableObject
    {
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private ObservableCollection<VideoFileInfo> _videoFiles = new();

        [ObservableProperty]
        private VideoFileInfo? _selectedVideo;

        partial void OnSelectedVideoChanged(VideoFileInfo? value)
        {
            DeleteVideoCommand.NotifyCanExecuteChanged();
            OpenVideoCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private string _statusMessage = "就绪";

        [ObservableProperty]
        private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        public RecordingManagerViewModel(
            LogService logService,
            SettingsService settingsService)
        {
            _logService = logService;
            _settingsService = settingsService;

            SavePath = _settingsService.SavePath;

            _logService.AddLog($"[录制管理] ViewModel 初始化 - 保存路径：{SavePath}");

            // 确保保存路径存在
            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                    _logService.AddLog($"[录制管理] 录制文件保存路径已创建：{SavePath}");
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"[录制管理] 创建保存路径失败：{ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                var files = Directory.GetFiles(SavePath, "*.mp4");
                _logService.AddLog($"[录制管理] 保存路径已存在，找到 {files.Length} 个 .mp4 文件");
            }
        }

        /// <summary>
        /// 刷新视频文件列表
        /// </summary>
        [RelayCommand]
        private void RefreshFileList()
        {
            try
            {
                VideoFiles.Clear();

                if (!Directory.Exists(SavePath))
                {
                    try
                    {
                        Directory.CreateDirectory(SavePath);
                        StatusMessage = $"已创建保存路径：{SavePath}";
                        _logService.AddLog($"[录制管理] 录制文件保存路径已创建：{SavePath}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"无法创建保存路径：{ex.Message}";
                        _logService.AddLog($"[录制管理] 创建保存路径失败：{ex.Message}", LogLevel.Error);
                        return;
                    }
                }

                _logService.AddLog($"[录制管理] 开始扫描目录：{SavePath}");

                // 搜索 MP4 和 AVI 文件
                var mp4Files = Directory.GetFiles(SavePath, "*.mp4");
                var aviFiles = Directory.GetFiles(SavePath, "*.avi");
                var files = mp4Files.Concat(aviFiles)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                _logService.AddLog($"[录制管理] 找到 {files.Count} 个视频文件");

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var videoInfo = new VideoFileInfo
                        {
                            FilePath = file,
                            FileName = fileInfo.Name,
                            CreatedTime = fileInfo.CreationTime,
                            ModifiedTime = fileInfo.LastWriteTime,
                            FileSizeKB = fileInfo.Length / 1024.0
                        };
                        VideoFiles.Add(videoInfo);
                        _logService.AddLog($"[录制管理] 加载：{fileInfo.Name} - {videoInfo.SizeDisplay}");
                    }
                    catch (Exception ex)
                    {
                        _logService.AddLog($"[录制管理] 加载文件失败：{file} - {ex.Message}", LogLevel.Error);
                    }
                }

                StatusMessage = VideoFiles.Count > 0
                    ? $"已加载 {VideoFiles.Count} 个视频文件"
                    : "暂无视频文件（录制文件将保存在此目录）";
                _logService.AddLog($"[录制管理] 刷新完成 - 成功加载 {VideoFiles.Count} 个文件");
            }
            catch (Exception ex)
            {
                StatusMessage = $"刷新失败：{ex.Message}";
                _logService.AddLog($"[录制管理] 刷新失败：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 删除选中的视频文件
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanDeleteVideo))]
        private void DeleteVideo()
        {
            if (SelectedVideo == null) return;

            var result = MessageBox.Show(
                $"确定要删除视频文件 \"{SelectedVideo.FileName}\" 吗？\n此操作不可恢复！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(SelectedVideo.FilePath);
                _logService.AddLog($"已删除视频文件：{SelectedVideo.FilePath}");
                VideoFiles.Remove(SelectedVideo);
                SelectedVideo = null;
                StatusMessage = "文件已删除";
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除失败：{ex.Message}";
                _logService.AddLog($"删除视频文件失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDeleteVideo() => SelectedVideo != null;

        /// <summary>
        /// 打开视频文件所在目录
        /// </summary>
        [RelayCommand]
        private void OpenFileLocation()
        {
            if (SelectedVideo == null)
            {
                if (Directory.Exists(SavePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", SavePath);
                }
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(SelectedVideo.FilePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", directory);
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"打开文件位置失败：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 使用系统默认播放器打开视频
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanOpenVideo))]
        private void OpenVideo()
        {
            if (SelectedVideo == null) return;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedVideo.FilePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                _logService.AddLog($"已打开视频：{SelectedVideo.FilePath}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"打开视频失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"打开视频失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanOpenVideo() => SelectedVideo != null;
    }
}
