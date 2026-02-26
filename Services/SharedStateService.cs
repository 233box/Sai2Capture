using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 共享状态服务
    /// 集中管理捕获相关的运行时状态
    /// </summary>
    public partial class SharedStateService : ObservableObject
    {
        /// <summary>
        /// 窗口底部裁剪像素值
        /// </summary>
        [ObservableProperty]
        private int _cutWindow = 70;

        /// <summary>
        /// 捕获运行状态标志
        /// </summary>
        [ObservableProperty]
        private bool _running = false;

        /// <summary>
        /// 当前帧序号
        /// </summary>
        [ObservableProperty]
        private int _frameNumber = 0;

        /// <summary>
        /// 上一帧图像缓存
        /// </summary>
        [ObservableProperty]
        private Mat? _lastImage = null;

        /// <summary>
        /// 输出文件夹路径
        /// </summary>
        [ObservableProperty]
        private string _outputFolder = "";

        /// <summary>
        /// 目标窗口句柄
        /// </summary>
        [ObservableProperty]
        private nint _hwnd = nint.Zero;

        /// <summary>
        /// 捕获间隔时间 (秒)
        /// </summary>
        [ObservableProperty]
        private double _interval = 0.5;

        /// <summary>
        /// 首次启动标志
        /// </summary>
        [ObservableProperty]
        private bool _isInitialized = false;

        /// <summary>
        /// 已保存帧计数
        /// </summary>
        [ObservableProperty]
        private int _savedCount = 0;

        /// <summary>
        /// OpenCV 视频写入器实例
        /// </summary>
        public VideoWriter? VideoWriter { get; set; }

        /// <summary>
        /// 输出视频文件路径
        /// </summary>
        public string? VideoPath { get; set; }

        /// <summary>
        /// SAI2 画布宽度
        /// </summary>
        [ObservableProperty]
        private int _canvasWidth = 0;

        /// <summary>
        /// SAI2 画布高度
        /// </summary>
        [ObservableProperty]
        private int _canvasHeight = 0;

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

            if (LastImage != null)
            {
                LastImage.Dispose();
                LastImage = null;
            }

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
