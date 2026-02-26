using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 共享状态服务 - 集中管理捕获相关的运行时状态
    /// </summary>
    public partial class SharedStateService : ObservableObject
    {
        [ObservableProperty] private int _cutWindow = 70;
        [ObservableProperty] private bool _running = false;
        [ObservableProperty] private int _frameNumber = 0;
        [ObservableProperty] private int _savedCount = 0;
        [ObservableProperty] private Mat? _lastImage;
        [ObservableProperty] private string _outputFolder = "";
        [ObservableProperty] private nint _hwnd = nint.Zero;
        [ObservableProperty] private double _interval = 0.5;
        [ObservableProperty] private bool _isInitialized = false;
        [ObservableProperty] private int _canvasWidth = 0;
        [ObservableProperty] private int _canvasHeight = 0;

        public VideoWriter? VideoWriter { get; set; }
        public string? VideoPath { get; set; }

        /// <summary>
        /// 重置所有捕获相关状态到初始值
        /// </summary>
        public void ResetCaptureState()
        {
            Running = false;
            FrameNumber = 0;
            SavedCount = 0;
            IsInitialized = false;
            Hwnd = nint.Zero;
            OutputFolder = "";
            VideoPath = null;

            LastImage?.Dispose();
            LastImage = null;

            if (VideoWriter != null)
            {
                try
                {
                    VideoWriter.Release();
                    VideoWriter.Dispose();
                }
                catch
                {
                    // 忽略已释放对象的异常
                }
                finally
                {
                    VideoWriter = null;
                }
            }
        }
    }
}
