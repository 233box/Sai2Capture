using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 窗口内容捕获服务
    /// 负责管理窗口捕获的全生命周期：
    /// 1. 初始化捕获参数
    /// 2. 控制捕获流程(开始/暂停/停止)
    /// 3. 维护捕获状态
    /// 4. 协调帧处理和视频生成
    /// </summary>
    public partial class CaptureService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly UtilityService _utilityService;
        private Thread? _captureThread;
        private Dispatcher? _dispatcher;

        /// <summary>
        /// 捕获状态描述
        /// 可能取值及其含义：
        /// "未开始" - 初始状态/空闲状态
        /// "开始捕获" - 启动捕获时的初始状态
        /// "已捕获帧 #{n}" - 捕获运行中状态
        /// "捕获已暂停" - 暂停状态（保留会话）
        /// "捕获停止" - 正常停止状态
        /// "捕获停止，视频已保存: {路径}" - 完成视频保存的状态
        /// "错误: {消息}" - 异常状态
        /// </summary>
        [ObservableProperty]
        private string _status = "未开始";

        /// <summary>
        /// 初始化捕获服务
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        /// <param name="windowCaptureService">窗口捕获服务</param>
        /// <param name="utilityService">工具服务</param>
        public CaptureService(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            UtilityService utilityService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
        }
        
        /// <summary>
        /// 初始化捕获服务的Dispatcher
        /// 用于在捕获线程中安全更新UI状态
        /// </summary>
        /// <param name="dispatcher">UI线程的Dispatcher对象</param>
        public void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// 开始捕获指定窗口的内容
        /// 1. 停止任何正在运行的捕获
        /// 2. 查找目标窗口句柄
        /// 3. 首次运行时初始化输出目录
        /// 4. 启动捕获线程
        /// </summary>
        /// <param name="windowTitle">目标窗口标题</param>
        /// <param name="useExactMatch">是否使用精确匹配窗口标题</param>
        /// <param name="interval">捕获间隔时间(秒)</param>
        /// <exception cref="Exception">捕获过程中的异常会显示错误消息</exception>
        public void StartCapture(
            string? windowTitle,
            bool useExactMatch,
            double interval)
        {
            try
            {
                if (string.IsNullOrEmpty(windowTitle))
                {
                    throw new ArgumentException("窗口标题不能为空", nameof(windowTitle));
                }
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
                System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = $"错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 暂停当前捕获过程
        /// 保持捕获状态但不继续捕获新帧
        /// 可随时通过StartCapture恢复捕获
        /// </summary>
        public void PauseCapture()
        {
            _sharedState.Running = false;
            Status = "捕获已暂停";
        }

        /// <summary>
        /// 完全停止捕获过程
        /// 1. 停止捕获线程
        /// 2. 释放视频写入器资源
        /// 3. 重置首次启动标志
        /// 4. 更新状态信息
        /// </summary>
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

        /// <summary>
        /// 捕获循环核心实现
        /// 在独立后台线程中运行，持续：
        /// 1. 通过窗口捕获服务获取窗口内容图像
        /// 2. 保存有变化的帧到输出目录
        /// 3. 通过Dispatcher线程安全更新UI状态
        /// 4. 按指定间隔控制捕获频率
        /// </summary>
        /// <remarks>
        /// 捕获过程中异常会被捕获并：
        /// 1. 更新状态显示错误信息
        /// 2. 线程休眠1秒后继续尝试
        /// </remarks>
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
