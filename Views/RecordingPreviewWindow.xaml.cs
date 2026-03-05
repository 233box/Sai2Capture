using OpenCvSharp;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sai2Capture.Styles;
using Sai2Capture.Models;
using Sai2Capture.Services;

namespace Sai2Capture.Views
{
    /// <summary>
    /// 录制预览窗口 - 支持播放控制和帧导航
    /// </summary>
    public partial class RecordingPreviewWindow : CustomDialogWindow
    {
        private readonly RecordingDataService _recordingDataService;
        private RecordingMetadata? _metadata;
        private string _filePath;
        private int _currentFrameIndex;
        private bool _isPlaying;
        private DispatcherTimer? _playTimer;
        private double _playSpeed = 1.0;
        private BitmapSource? _cachedBitmap;
        private bool _isDraggingSlider; // 标记是否正在拖动滑块
        private bool _isLoadingFrame; // 标记是否正在加载帧

        // 当前帧信息
        public string FileName { get; private set; } = string.Empty;
        public string FrameInfoText { get; private set; } = string.Empty;
        public string CurrentFrameInfoText { get; private set; } = string.Empty;
        public int CurrentFrameNumber { get; private set; }
        public int TotalFrames { get; private set; }

        public RecordingPreviewWindow(string filePath)
        {
            InitializeComponent();
            DataContext = this;

            _recordingDataService = Ioc.Default.GetRequiredService<RecordingDataService>();
            _filePath = filePath;
            _currentFrameIndex = 1;

            InitializePreview();
        }

        private async void InitializePreview()
        {
            try
            {
                // 加载元数据
                _metadata = _recordingDataService.LoadMetadata(_filePath);
                if (_metadata == null || _metadata.Frames.Count == 0)
                {
                    ShowError("无法加载录制文件元数据");
                    return;
                }

                FileName = Path.GetFileName(_filePath);
                var duration = _metadata.EndTime.HasValue
                    ? (_metadata.EndTime.Value - _metadata.StartTime).TotalSeconds
                    : 0;
                FrameInfoText = $"{_metadata.TotalFrames} 帧 | {duration:F1} 秒 | {_metadata.CanvasWidth}x{_metadata.CanvasHeight}";

                TotalFrames = _metadata.TotalFrames;
                CurrentFrameNumber = 1;
                CurrentFrameInfoText = $"帧 1 / {_metadata.TotalFrames}";
                if (CurrentFrameInfoTextBlock != null)
                {
                    CurrentFrameInfoTextBlock.Text = CurrentFrameInfoText;
                }

                FrameSlider.Minimum = 1;
                FrameSlider.Maximum = _metadata.TotalFrames;
                FrameSlider.Value = 1;

                // 显示初始加载提示
                EmptyHintText.Text = "加载中...";
                EmptyHintText.Visibility = System.Windows.Visibility.Visible;

                // 异步加载第一帧
                await LoadFrameAsync(1);
            }
            catch (Exception ex)
            {
                ShowError($"加载预览失败：{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadFrameAsync(int frameIndex, bool isPlaying = false)
        {
            // 防止重复加载
            if (_isLoadingFrame) return;
            if (_metadata == null || frameIndex < 1 || frameIndex > _metadata.Frames.Count)
                return;

            _isLoadingFrame = true;

            try
            {
                // 在后台线程加载和解码图像
                var newBitmap = await System.Threading.Tasks.Task.Run(() =>
                {
                    var frameInfo = _metadata.Frames.FirstOrDefault(f => f.FrameIndex == frameIndex);
                    if (frameInfo == null) return null;

                    using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fileStream);

                    fileStream.Position = frameInfo.DataOffset;
                    var dataLength = reader.ReadInt32();
                    var jpegData = reader.ReadBytes(dataLength);

                    // 解码帧数据
                    using var decodedFrame = OpenCvSharp.Cv2.ImDecode(jpegData, OpenCvSharp.ImreadModes.Color);

                    if (decodedFrame != null && !decodedFrame.Empty())
                    {
                        using var memoryStream = new MemoryStream();
                        OpenCvSharp.Cv2.ImEncode(".png", decodedFrame, out var imageData);
                        memoryStream.Write(imageData, 0, imageData.Length);
                        memoryStream.Seek(0, System.IO.SeekOrigin.Begin);

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = memoryStream;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return bitmap;
                    }

                    return null;
                });

                // 只有加载成功后才更新 UI
                if (newBitmap != null)
                {
                    _cachedBitmap = newBitmap;
                    PreviewImage.Source = _cachedBitmap;
                    
                    // 播放时不显示加载提示，避免闪烁
                    if (!isPlaying)
                    {
                        EmptyHintText.Visibility = System.Windows.Visibility.Collapsed;
                    }
                    
                    // 更新帧信息
                    CurrentFrameNumber = frameIndex;
                    CurrentFrameInfoText = $"帧 {frameIndex} / {_metadata?.TotalFrames}";
                    if (CurrentFrameInfoTextBlock != null)
                    {
                        CurrentFrameInfoTextBlock.Text = CurrentFrameInfoText;
                    }
                    FrameSlider.Value = frameIndex;
                }
                else
                {
                    ShowError("无法解码帧数据");
                }
            }
            catch (Exception ex)
            {
                ShowError($"加载帧失败：{ex.Message}");
            }
            finally
            {
                _isLoadingFrame = false;
            }
        }

        private void ShowError(string message)
        {
            EmptyHintText.Text = message;
            EmptyHintText.Visibility = System.Windows.Visibility.Visible;
        }

        private void PlayPause()
        {
            if (_metadata == null || _metadata.Frames.Count == 0)
                return;

            _isPlaying = !_isPlaying;
            PlayPauseButton.Content = _isPlaying ? "⏸️" : "▶️";

            if (_isPlaying)
            {
                if (_playTimer == null)
                {
                    _playTimer = new DispatcherTimer();
                    _playTimer.Tick += PlayTimer_Tick;
                }

                var baseInterval = _metadata.CaptureInterval > 0 
                    ? _metadata.CaptureInterval 
                    : 0.5;
                var interval = baseInterval / _playSpeed;
                _playTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.05, interval));
                _playTimer.Start();
            }
            else
            {
                _playTimer?.Stop();
            }
        }

        private void PlayTimer_Tick(object? sender, EventArgs e)
        {
            if (_metadata == null) return;

            var nextFrame = _currentFrameIndex + 1;
            if (nextFrame > _metadata.Frames.Count)
            {
                // 播放到末尾，停止播放
                StopPlayback();
                return;
            }

            _currentFrameIndex = nextFrame;
            // 播放时不阻塞，直接加载下一帧
            _ = LoadFrameAsync(_currentFrameIndex, isPlaying: true);
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            _playTimer?.Stop();
            PlayPauseButton.Content = "▶️";
        }

        private void GoToFrame(int frameIndex)
        {
            // 如果正在加载或拖动中，跳过
            if (_isLoadingFrame || _isDraggingSlider) return;
            if (_metadata == null || frameIndex < 1 || frameIndex > _metadata.Frames.Count)
                return;

            StopPlayback();
            _currentFrameIndex = frameIndex;
            _ = LoadFrameAsync(frameIndex);
        }

        private void UpdatePlaySpeed()
        {
            var selectedItem = SpeedComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var speedText = selectedItem.Content.ToString();
                if (double.TryParse(speedText?.Replace("x", ""), out var speed))
                {
                    _playSpeed = speed;
                    // 更新速度信息显示
                    if (PlaySpeedInfoTextBlock != null)
                    {
                        PlaySpeedInfoTextBlock.Text = _playSpeed != 1.0 ? $"{_playSpeed}x" : "";
                    }

                    if (_isPlaying)
                    {
                        var baseInterval = _metadata?.CaptureInterval > 0
                            ? _metadata.CaptureInterval
                            : 0.5;
                        var interval = baseInterval / _playSpeed;
                        if (_playTimer != null)
                        {
                            _playTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.05, interval));
                        }
                    }
                }
            }
        }

        private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 确保控件已初始化
            if (PlaySpeedInfoTextBlock == null) return;
            UpdatePlaySpeed();
        }

        #region Event Handlers

        private void FirstFrameButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            GoToFrame(1);
        }

        private void PrevFrameButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_currentFrameIndex > 1)
            {
                GoToFrame(_currentFrameIndex - 1);
            }
        }

        private void PlayPauseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            PlayPause();
        }

        private void NextFrameButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_metadata != null && _currentFrameIndex < _metadata.Frames.Count)
            {
                GoToFrame(_currentFrameIndex + 1);
            }
        }

        private void LastFrameButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_metadata != null)
            {
                GoToFrame(_metadata.Frames.Count);
            }
        }

        private void FrameSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            // 仅在拖动结束后加载帧，避免拖动过程中频繁加载
            if (_metadata != null && e.NewValue != e.OldValue && !_isDraggingSlider)
            {
                GoToFrame((int)e.NewValue);
            }
        }

        private void FrameSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void FrameSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            // 拖动结束后加载帧
            GoToFrame((int)FrameSlider.Value);
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            StopPlayback();
            _playTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
