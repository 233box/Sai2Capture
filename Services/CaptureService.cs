using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
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
        private readonly LogService _logService;
        private Thread? _captureThread;
        private Dispatcher? _dispatcher;

        /// <summary>
        /// 获取共享状态服务
        /// 用于访问捕获状态信息
        /// </summary>
        public SharedStateService SharedState => _sharedState;
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
        /// <param name="logService">日志服务</param>
        public CaptureService(
            SharedStateService sharedState,
            WindowCaptureService windowCaptureService,
            UtilityService utilityService,
            LogService logService)
        {
            _sharedState = sharedState;
            _windowCaptureService = windowCaptureService;
            _utilityService = utilityService;
            _logService = logService;
        }
        
        /// <summary>
        /// 初始化捕获服务的Dispatcher
        /// 用于在捕获线程中安全更新UI状态
        /// </summary>
        /// <param name="dispatcher">UI线程的Dispatcher对象</param>
        public void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _logService.AddLog("捕获服务已初始化");
        }

        /// <summary>
        /// 开始捕获指定窗口的内容
        /// 1. 检查是否已在捕获中，如果是则直接返回
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
                // 如果已经在捕获中，直接返回
                if (_sharedState.Running)
                {
                    _logService.AddLog("捕获已在运行中，忽略启动请求", LogLevel.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(windowTitle))
                {
                    _logService.AddLog("窗口标题为空，无法启动捕获", LogLevel.Error);
                    throw new ArgumentException("窗口标题不能为空", nameof(windowTitle));
                }

                _logService.AddLog($"查找窗口: {windowTitle}");
                _sharedState.Hwnd = _windowCaptureService.FindWindowByTitle(windowTitle);
                _sharedState.Interval = interval;

                // 首次启动时初始化（IsInitialized=false 表示还未初始化）
                if (!_sharedState.IsInitialized)
                {
                    _logService.AddLog("首次启动，创建输出目录和视频写入器");
                    
                    // 使用绝对路径确保文件保存在正确的位置
                    var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                    _logService.AddLog($"应用程序基础目录: {baseDir}");
                    
                    _sharedState.OutputFolder = System.IO.Path.Combine(baseDir, "output_frames");
                    _logService.AddLog($"输出文件夹完整路径: {_sharedState.OutputFolder}");
                    
                    // 确保输出目录存在
                    if (!System.IO.Directory.Exists(_sharedState.OutputFolder))
                    {
                        System.IO.Directory.CreateDirectory(_sharedState.OutputFolder);
                        _logService.AddLog($"已创建输出目录: {_sharedState.OutputFolder}");
                    }
                    else
                    {
                        _logService.AddLog($"输出目录已存在: {_sharedState.OutputFolder}");
                    }
                    
                    _sharedState.VideoPath = _utilityService.GetUniqueVideoPath(
                        _sharedState.OutputFolder, 
                        "output", 
                        ".mp4");
                    _logService.AddLog($"视频完整路径: {_sharedState.VideoPath}");
                    
                    // 初始化 WGC 捕获会话
                    _logService.AddLog("初始化 WGC 捕获会话");
                    var wgcInitialized = _windowCaptureService.InitializeWgcCaptureAsync(_sharedState.Hwnd).Result;
                    if (wgcInitialized)
                    {
                        _logService.AddLog("WGC 捕获会话初始化成功，将使用 WGC API 进行截图");
                    }
                    else
                    {
                        _logService.AddLog("WGC 捕获会话初始化失败，将使用传统 PrintWindow API", LogLevel.Warning);
                    }
                    
                    // 创建视频写入器
                    InitializeVideoWriter();
                    
                    // 标记为已初始化，下次启动时跳过初始化步骤
                    // 注意：IsInitialized=true 表示"已经完成首次启动的初始化"
                    _sharedState.IsInitialized = true;
                    _logService.AddLog("首次启动初始化完成，IsInitialized 标志已设置为 true");
                }

                _sharedState.Running = true;
                Status = "开始捕获";
                _logService.AddLog($"捕获线程启动 - 窗口句柄: {_sharedState.Hwnd}, 间隔: {interval}秒");

                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true
                };
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"启动捕获失败: {ex.Message}", LogLevel.Error);
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
            _logService.AddLog($"捕获已暂停 - 已捕获 {_sharedState.FrameNumber} 帧", LogLevel.Warning);
        }

        /// <summary>
        /// 完全停止捕获过程
        /// 1. 停止捕获线程
        /// 2. 停止 WGC 捕获会话
        /// 3. 释放视频写入器资源
        /// 4. 重置所有状态到初始值
        /// 5. 更新状态信息
        /// </summary>
        public void StopCapture()
        {
            _sharedState.Running = false;
            
            string? savedVideoPath = _sharedState.VideoPath;
            bool hasVideo = _sharedState.VideoWriter != null;
            int totalFrames = _sharedState.FrameNumber;
            int savedFrames = _sharedState.SavedCount;
            
            // 停止 WGC 捕获会话
            _logService.AddLog("停止 WGC 捕获会话");
            _windowCaptureService.StopWgcCapture();
            
            if (hasVideo)
            {
                _logService.AddLog($"释放视频写入器 - 总帧数: {totalFrames}, 有效捕获: {savedFrames}");
                
                // 确保 VideoWriter 正确关闭并刷新缓冲区
                try
                {
                    if (_sharedState.VideoWriter != null)
                    {
                        _logService.AddLog("调用 VideoWriter.Release() 刷新缓冲区...");
                        _sharedState.VideoWriter.Release();
                        
                        _logService.AddLog("调用 VideoWriter.Dispose() 释放资源...");
                        _sharedState.VideoWriter.Dispose();
                        _sharedState.VideoWriter = null;
                        
                        // 等待一小段时间确保文件写入完成
                        System.Threading.Thread.Sleep(100);
                        
                        // 验证文件是否存在
                        if (!string.IsNullOrEmpty(savedVideoPath) && System.IO.File.Exists(savedVideoPath))
                        {
                            var fileInfo = new System.IO.FileInfo(savedVideoPath);
                            _logService.AddLog($"✓ 视频文件已成功保存: {savedVideoPath}");
                            _logService.AddLog($"  文件大小: {fileInfo.Length / 1024.0:F2} KB");
                            Status = $"捕获停止，视频已保存: {savedVideoPath}";
                        }
                        else
                        {
                            _logService.AddLog($"⚠ 警告: 视频文件不存在: {savedVideoPath}", LogLevel.Warning);
                            Status = $"捕获停止，但视频文件未找到";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"释放视频写入器时出错: {ex.Message}", LogLevel.Error);
                    Status = $"捕获停止，但保存视频时出错";
                }
            }
            else
            {
                Status = "捕获停止";
                _logService.AddLog("捕获停止 - 未生成视频文件");
            }

            // 重置所有状态到初始值
            _sharedState.ResetCaptureState();
            _logService.AddLog("捕获状态已重置到初始值");
        }

        /// <summary>
        /// 初始化视频写入器
        /// 在第一次录制时创建VideoWriter实例
        /// 使用MP4V编码器，帧率根据捕获间隔自动计算
        /// </summary>
        private void InitializeVideoWriter()
        {
            try
            {
                if (string.IsNullOrEmpty(_sharedState.VideoPath))
                {
                    throw new Exception("视频路径未设置");
                }
                
                _logService.AddLog($"准备创建视频写入器 - 路径: {_sharedState.VideoPath}");
                
                // 确保输出目录存在
                var directory = System.IO.Path.GetDirectoryName(_sharedState.VideoPath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    _logService.AddLog($"输出目录不存在，正在创建: {directory}");
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                // 获取第一帧以确定视频尺寸
                _logService.AddLog("捕获第一帧以确定视频尺寸...");
                var firstFrame = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);
                
                if (firstFrame == null || firstFrame.Empty())
                {
                    throw new Exception("无法捕获第一帧或帧为空");
                }
                
                int width = firstFrame.Width;
                int height = firstFrame.Height;
                _logService.AddLog($"第一帧尺寸: {width}x{height}");
                
                if (width <= 0 || height <= 0)
                {
                    throw new Exception($"无效的帧尺寸: {width}x{height}");
                }
                
                // 根据捕获间隔计算帧率: FPS = 1 / 间隔
                // 例如: 间隔0.1秒 -> 10 FPS, 间隔0.5秒 -> 2 FPS
                double fps = 20;//_sharedState.Interval > 0 ? 1.0 / _sharedState.Interval : 10.0;
                _logService.AddLog($"计算帧率: {fps:F2} FPS (间隔: {_sharedState.Interval}秒)");
                
                // 创建视频写入器
                // 使用MP4V编码器
                var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');
                _logService.AddLog($"使用编码器: MP4V (FourCC: {fourcc})");
                
                _sharedState.VideoWriter = new VideoWriter(
                    _sharedState.VideoPath,
                    fourcc,
                    fps,
                    new OpenCvSharp.Size(width, height));
                
                if (!_sharedState.VideoWriter.IsOpened())
                {
                    _logService.AddLog("VideoWriter.IsOpened() 返回 false", LogLevel.Error);
                    _logService.AddLog("可能的原因:", LogLevel.Error);
                    _logService.AddLog("1. 输出目录不存在或无写入权限", LogLevel.Error);
                    _logService.AddLog("2. MP4V 编码器不可用", LogLevel.Error);
                    _logService.AddLog("3. 视频尺寸无效", LogLevel.Error);
                    _logService.AddLog($"4. 文件路径过长或包含非法字符: {_sharedState.VideoPath}", LogLevel.Error);
                    throw new Exception("无法创建视频写入器 - VideoWriter 初始化失败");
                }
                
                _logService.AddLog($"✓ 视频写入器已成功创建 - 尺寸: {width}x{height}, FPS: {fps:F2}");
                
                // 释放第一帧
                firstFrame.Dispose();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"创建视频写入器失败: {ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈: {ex.StackTrace}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 捕获循环核心实现
        /// 在独立后台线程中运行，持续：
        /// 1. 通过窗口捕获服务获取窗口内容图像（使用 WGC API）
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
            _logService.AddLog("捕获循环开始");
            int errorCount = 0;
            
            try
            {
                while (_sharedState.Running)
                {
                    try
                    {
                        var image = _windowCaptureService.CaptureWindowContent(_sharedState.Hwnd);
                        _windowCaptureService.SaveIfModified(image);

                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"已捕获帧 #{_sharedState.FrameNumber}";
                        });

                        // 每10帧记录一次日志
                        if (_sharedState.FrameNumber % 10 == 0)
                        {
                            _logService.AddLog($"捕获进度: {_sharedState.FrameNumber} 帧, 已保存: {_sharedState.SavedCount} 帧");
                        }

                        _sharedState.FrameNumber++;
                        Thread.Sleep((int)(_sharedState.Interval * 1000));
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logService.AddLog($"捕获错误 (总计: {errorCount}): {ex.Message}", LogLevel.Error);
                        
                        _dispatcher?.Invoke(() =>
                        {
                            Status = $"捕获错误: {ex.Message}";
                        });
                        Thread.Sleep(1000);
                    }
                }
                
                _logService.AddLog($"捕获循环结束 - 总帧数: {_sharedState.FrameNumber}, 已保存: {_sharedState.SavedCount} 帧, 错误次数: {errorCount}");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"捕获循环致命错误: {ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈: {ex.StackTrace}", LogLevel.Error);
                
                _dispatcher?.Invoke(() =>
                {
                    Status = $"捕获致命错误: {ex.Message}";
                });
            }
        }
    }
}
