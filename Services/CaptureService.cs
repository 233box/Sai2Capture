using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    public partial class CaptureService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private Thread? _captureThread;
        private Dispatcher? _dispatcher;

        [ObservableProperty]
        private string _status = "未开始";

        public CaptureService(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            UtilityService utilityService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
        }
        
        public void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void StartCapture(
            string windowTitle,
            bool useExactMatch,
            double interval)
        {
            try
            {
                StopCapture(); // 确保停止任何正在运行的捕获

                _sharedState.Hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                _sharedState.Interval = interval;

                // 首次启动时初始化
                if (!_sharedState.FirstStart)
                {
                    _sharedState.OutputFolder = _utilityService.CreateOutputFolder();
                    _sharedState.VideoPath = _utilityService.GetUniqueVideoPath(
                        _sharedState.OutputFolder, 
                        "output", 
                        ".mp4");
                    _sharedState.FirstStart = true;
                }

                _sharedState.Running = true;
                Status = "开始捕获";

                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true
                };
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = $"错误: {ex.Message}";
            }
        }

        public void PauseCapture()
        {
            _sharedState.Running = false;
            Status = "捕获已暂停";
        }

        public void StopCapture()
        {
            _sharedState.Running = false;
            
            if (_sharedState.VideoWriter != null)
            {
                _sharedState.VideoWriter.Release();
                _sharedState.VideoWriter = null;
                Status = $"捕获停止，视频已保存: {_sharedState.VideoPath}";
            }
            else
            {
                Status = "捕获停止";
            }

            _sharedState.FirstStart = false;
        }

        private void CaptureLoop()
        {
            try
            {
                while (_sharedState.Running)
                {
                    var image = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);
                    _windowCaptureService.SaveIfModified(image);

                    _dispatcher?.Invoke(() =>
                    {
                        Status = $"已捕获帧 #{_sharedState.FrameNumber}";
                    });

                    _sharedState.FrameNumber++;
                    Thread.Sleep((int)(_sharedState.Interval * 1000));
                }
            }
            catch (Exception ex)
            {
                _dispatcher?.Invoke(() =>
                {
                    Status = $"捕获错误: {ex.Message}";
                });
                Thread.Sleep(1000);
            }
        }
    }
}
