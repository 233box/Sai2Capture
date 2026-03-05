using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sai2Capture.Models;
using Sai2Capture.Services;
using Sai2Capture.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 录制文件信息（用于 UI 显示）
    /// </summary>
    public class RecordingFileInfo : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int TotalFrames { get; set; }
        public double FileSizeKB { get; set; }
        public double CaptureInterval { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public string DurationDisplay => EndTime.HasValue 
            ? $"{(EndTime.Value - StartTime).TotalMinutes:F1} 分钟" 
            : "未完成";
        public string SizeDisplay => FileSizeKB > 1024 
            ? $"{FileSizeKB / 1024:F2} MB" 
            : $"{FileSizeKB:F2} KB";
        public string ResolutionDisplay => $"{CanvasWidth} x {CanvasHeight}";
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// 录制文件管理视图模型
    /// </summary>
    public partial class RecordingManagerViewModel : ObservableObject
    {
        private readonly RecordingDataService _recordingDataService;
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private readonly CaptureService _captureService;

        [ObservableProperty]
        private ObservableCollection<RecordingFileInfo> _recordingFiles = new();

        [ObservableProperty]
        private RecordingFileInfo? _selectedRecording;

        partial void OnSelectedRecordingChanged(RecordingFileInfo? value)
        {
            // 当选中项变化时，通知命令重新评估 CanExecute
            ResumeRecordingCommand.NotifyCanExecuteChanged();
            ExportVideoCommand.NotifyCanExecuteChanged();
            DeleteRecordingCommand.NotifyCanExecuteChanged();
            PreviewRecordingCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private string _statusMessage = "就绪";

        [ObservableProperty]
        private bool _isExporting = false;

        partial void OnIsExportingChanged(bool value)
        {
            // 当导出状态变化时，通知命令重新评估 CanExecute
            ResumeRecordingCommand.NotifyCanExecuteChanged();
            ExportVideoCommand.NotifyCanExecuteChanged();
            DeleteRecordingCommand.NotifyCanExecuteChanged();
            PreviewRecordingCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private double _exportProgress = 0;

        [ObservableProperty]
        private string _exportProgressText = string.Empty;

        [ObservableProperty]
        private double _exportFps = 20;

        [ObservableProperty]
        private string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        public RecordingManagerViewModel(
            RecordingDataService recordingDataService,
            LogService logService,
            SettingsService settingsService,
            CaptureService captureService)
        {
            _recordingDataService = recordingDataService;
            _logService = logService;
            _settingsService = settingsService;
            _captureService = captureService;
            
            // 从设置服务获取保存路径，确保与录制服务使用相同的路径
            SavePath = _settingsService.SavePath;
            _logService.AddLog($"[录制管理] ViewModel 初始化 - 保存路径：{SavePath}", LogLevel.Warning);
            _logService.AddLog($"[录制管理] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}", LogLevel.Warning);
            
            // 确保保存路径存在
            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                    _logService.AddLog($"[录制管理] 录制文件保存路径已创建：{SavePath}", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"[录制管理] 创建保存路径失败：{ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                var files = Directory.GetFiles(SavePath, "*.sai2rec");
                _logService.AddLog($"[录制管理] 保存路径已存在，找到 {files.Length} 个 .sai2rec 文件", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 刷新录制文件列表
        /// </summary>
        [RelayCommand]
        private void RefreshFileList()
        {
            try
            {
                RecordingFiles.Clear();

                // 确保保存路径存在
                if (!Directory.Exists(SavePath))
                {
                    try
                    {
                        Directory.CreateDirectory(SavePath);
                        StatusMessage = $"已创建保存路径：{SavePath}";
                        _logService.AddLog($"[录制管理] 录制文件保存路径已创建：{SavePath}", LogLevel.Warning);
                        return;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"无法创建保存路径：{ex.Message}";
                        _logService.AddLog($"[录制管理] 创建保存路径失败：{ex.Message}", LogLevel.Error);
                        return;
                    }
                }

                _logService.AddLog($"[录制管理] 开始扫描目录：{SavePath}", LogLevel.Info);
                
                var files = Directory.GetFiles(SavePath, "*.sai2rec")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                _logService.AddLog($"[录制管理] 找到 {files.Count} 个 .sai2rec 文件", LogLevel.Info);

                foreach (var file in files)
                {
                    _logService.AddLog($"[录制管理] 加载文件：{file}", LogLevel.Info);
                    
                    var metadata = _recordingDataService.LoadMetadata(file);
                    if (metadata != null)
                    {
                        var fileInfo = new FileInfo(file);
                        RecordingFiles.Add(new RecordingFileInfo
                        {
                            FilePath = file,
                            FileName = fileInfo.Name,
                            WindowTitle = metadata.WindowTitle,
                            StartTime = metadata.StartTime,
                            EndTime = metadata.EndTime,
                            TotalFrames = metadata.TotalFrames,
                            FileSizeKB = fileInfo.Length / 1024.0,
                            CaptureInterval = metadata.CaptureInterval,
                            CanvasWidth = metadata.CanvasWidth,
                            CanvasHeight = metadata.CanvasHeight
                        });
                        _logService.AddLog($"[录制管理] ✓ 成功加载：{fileInfo.Name} - {metadata.TotalFrames} 帧", LogLevel.Info);
                    }
                    else
                    {
                        _logService.AddLog($"[录制管理] ✗ 加载失败：{file} - LoadMetadata 返回 null", LogLevel.Error);
                        
                        // 尝试直接读取文件信息来调试
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            _logService.AddLog($"[录制管理] 文件信息：大小={fileInfo.Length} 字节，修改时间={fileInfo.LastWriteTime}", LogLevel.Error);
                            
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                            var magicBuffer = new byte[9];
                            fs.Read(magicBuffer, 0, 9);
                            var magic = System.Text.Encoding.ASCII.GetString(magicBuffer);
                            _logService.AddLog($"[录制管理] 文件魔数：{magic}", LogLevel.Error);
                            
                            fs.Position = 13;
                            var metaOffset = new BinaryReader(fs).ReadInt64();
                            _logService.AddLog($"[录制管理] 元数据偏移：{metaOffset}", LogLevel.Error);
                        }
                        catch (Exception ex)
                        {
                            _logService.AddLog($"[录制管理] 调试信息读取失败：{ex.Message}", LogLevel.Error);
                        }
                    }
                }

                StatusMessage = RecordingFiles.Count > 0
                    ? $"已加载 {RecordingFiles.Count} 个录制文件"
                    : "暂无录制文件（录制文件将保存在此目录）";
                _logService.AddLog($"[录制管理] 刷新完成 - 成功加载 {RecordingFiles.Count} 个文件", LogLevel.Info);
            }
            catch (Exception ex)
            {
                StatusMessage = $"刷新失败：{ex.Message}";
                _logService.AddLog($"[录制管理] 刷新失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"[录制管理] 异常堆栈：{ex.StackTrace}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 导出为视频
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExportVideo))]
        private void ExportVideo()
        {
            if (SelectedRecording == null) return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "MP4 视频 (*.mp4)|*.mp4|AVI 视频 (*.avi)|*.avi",
                    FileName = Path.ChangeExtension(SelectedRecording.FileName, ".mp4"),
                    InitialDirectory = SavePath
                };

                if (dialog.ShowDialog() != true) return;

                IsExporting = true;
                ExportProgress = 0;
                ExportProgressText = "正在导出视频...";

                // 在新线程中执行导出
                Task.Run(() =>
                {
                    var success = _recordingDataService.ExportToVideo(
                        SelectedRecording.FilePath,
                        dialog.FileName,
                        ExportFps);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsExporting = false;
                        if (success)
                        {
                            StatusMessage = $"视频导出成功：{dialog.FileName}";
                            ExportProgressText = "导出完成";
                            _logService.AddLog($"视频导出成功：{dialog.FileName}");
                            MessageBox.Show($"视频已成功导出到：\n{dialog.FileName}", "导出成功",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            StatusMessage = "视频导出失败";
                            ExportProgressText = "导出失败";
                            _logService.AddLog("视频导出失败", LogLevel.Error);
                            MessageBox.Show("视频导出失败，请查看日志获取详细信息", "导出失败",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                IsExporting = false;
                StatusMessage = $"导出错误：{ex.Message}";
                _logService.AddLog($"导出视频错误：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"导出错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExportVideo() => SelectedRecording != null && !IsExporting;

        /// <summary>
        /// 从选中的录制文件继续录制
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanResumeRecording))]
        private void ResumeRecording()
        {
            if (SelectedRecording == null) return;

            try
            {
                _logService.AddLog($"从文件继续录制：{SelectedRecording.FilePath}");
                _logService.AddLog($"原录制信息：{SelectedRecording.TotalFrames} 帧，{SelectedRecording.WindowTitle}");

                // 通过 CaptureService 继续录制
                // 注意：这里需要 MainViewModel 配合，通过事件或回调实现
                ResumeRecordingRequested?.Invoke(this, new ResumeRecordingEventArgs
                {
                    FilePath = SelectedRecording.FilePath,
                    WindowTitle = SelectedRecording.WindowTitle,
                    ExistingFrames = SelectedRecording.TotalFrames
                });

                StatusMessage = $"已从 {SelectedRecording.FileName} 继续录制";
            }
            catch (Exception ex)
            {
                StatusMessage = $"继续录制失败：{ex.Message}";
                _logService.AddLog($"继续录制失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"继续录制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanResumeRecording() => SelectedRecording != null && !IsExporting;

        /// <summary>
        /// 删除选中的录制文件
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanDeleteRecording))]
        private void DeleteRecording()
        {
            if (SelectedRecording == null) return;

            var result = MessageBox.Show(
                $"确定要删除录制文件 \"{SelectedRecording.FileName}\" 吗？\n\n此操作不可恢复！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(SelectedRecording.FilePath);
                _logService.AddLog($"已删除录制文件：{SelectedRecording.FilePath}");
                RecordingFiles.Remove(SelectedRecording);
                SelectedRecording = null;
                StatusMessage = "文件已删除";
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除失败：{ex.Message}";
                _logService.AddLog($"删除录制文件失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDeleteRecording() => SelectedRecording != null && !IsExporting;

        /// <summary>
        /// 打开录制文件所在目录
        /// </summary>
        [RelayCommand]
        private void OpenFileLocation()
        {
            if (SelectedRecording == null)
            {
                // 打开保存路径
                if (Directory.Exists(SavePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", SavePath);
                }
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(SelectedRecording.FilePath);
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
        /// 预览选中的录制文件（显示第一帧）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPreviewRecording))]
        private void PreviewRecording()
        {
            if (SelectedRecording == null) return;

            try
            {
                var metadata = _recordingDataService.LoadMetadata(SelectedRecording.FilePath);
                if (metadata == null || metadata.Frames.Count == 0)
                {
                    MessageBox.Show("无法加载录制文件预览", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 读取第一帧用于预览
                using var fileStream = new FileStream(SelectedRecording.FilePath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fileStream);

                var firstFrame = metadata.Frames.OrderBy(f => f.FrameIndex).FirstOrDefault();
                if (firstFrame == null)
                {
                    MessageBox.Show("录制文件中无有效帧", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                fileStream.Position = firstFrame.DataOffset;
                var dataLength = reader.ReadInt32();
                var jpegData = reader.ReadBytes(dataLength);

                using var frame = OpenCvSharp.Cv2.ImDecode(jpegData, OpenCvSharp.ImreadModes.Color);
                if (frame == null || frame.Empty())
                {
                    MessageBox.Show("无法解码帧数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 显示预览窗口
                var previewWindow = new RecordingPreviewWindow(frame);
                previewWindow.Owner = System.Windows.Application.Current.MainWindow;
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"预览录制文件失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanPreviewRecording() => SelectedRecording != null && !IsExporting;

        /// <summary>
        /// 当请求继续录制时触发
        /// </summary>
        public event EventHandler<ResumeRecordingEventArgs>? ResumeRecordingRequested;
    }

    /// <summary>
    /// 继续录制事件参数
    /// </summary>
    public class ResumeRecordingEventArgs : EventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public int ExistingFrames { get; set; }
    }
}
