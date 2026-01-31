using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.Collections.Generic;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 共享状态服务
    /// 集中管理应用程序的所有运行时状态
    /// 包括捕获状态、窗口信息、视频生成设置等
    /// </summary>
    public partial class SharedStateService : ObservableObject
    {
        /// <summary>
        /// 窗口底部裁剪像素值
        /// 用于调整捕获区域，避免任务栏干扰
        /// 默认值70像素
        /// </summary>
        [ObservableProperty]
        private int _cutWindow = 70;

        /// <summary>
        /// 捕获运行状态标志
        /// true表示正在捕获，false表示已停止
        /// 控制捕获循环的执行
        /// </summary>
        [ObservableProperty]
        private bool _running = false;

        /// <summary>
        /// 当前帧序号
        /// 从0开始递增，记录已捕获的帧数
        /// 用于状态显示和文件名生成
        /// </summary>
        [ObservableProperty]
        private int _frameNumber = 0;

        /// <summary>
        /// 上一帧图像缓存
        /// 用于比较帧间差异，决定是否保存
        /// 类型为OpenCV Mat对象
        /// </summary>
        [ObservableProperty]
        private Mat? _lastImage = null;

        /// <summary>
        /// 窗口置顶状态
        /// true: 主窗口保持在最前
        /// false: 正常窗口行为
        /// </summary>
        [ObservableProperty]
        private bool _isTopmost = false;

        /// <summary>
        /// 输出文件夹路径
        /// 存储捕获的帧图像和生成的视频文件
        /// 默认路径为output_frames/时间戳
        /// </summary>
        [ObservableProperty]
        private string _outputFolder = "";

        /// <summary>
        /// 目标窗口句柄
        /// 通过FindWindow API获取的窗口唯一标识符
        /// 用于所有窗口相关的API调用
        /// nint.Zero表示未设置有效窗口
        /// </summary>
        [ObservableProperty]
        private nint _hwnd = nint.Zero;

        /// <summary>
        /// 捕获间隔时间(秒)
        /// 控制两次捕获之间的等待时间
        /// 默认0.5秒(2FPS)
        /// </summary>
        [ObservableProperty]
        private double _interval = 0.5;

        /// <summary>
        /// 首次启动标志
        /// true: 需要进行初始设置
        /// false: 已完成初始化
        /// 控制输出目录和视频文件的创建时机
        /// </summary>
        [ObservableProperty]
        private bool _firstStart = false;

        /// <summary>
        /// 已保存帧计数
        /// 记录实际被写入视频文件的帧数
        /// 用于统计和进度显示
        /// </summary>
        [ObservableProperty]
        private int _savedCount = 0;

        /// <summary>
        /// OpenCV视频写入器实例
        /// 负责将捕获的帧写入视频文件
        /// 在捕获开始时创建，结束时释放
        /// </summary>
        public VideoWriter? VideoWriter { get; set; }

        /// <summary>
        /// 输出视频文件路径
        /// 最终生成的MP4视频文件位置
        /// 包含完整路径和文件名
        /// </summary>
        public string? VideoPath { get; set; }

        /// <summary>
        /// 窗口标题列表缓存
        /// 存储当前系统中所有可见窗口的标题
        /// 用于用户界面中的窗口选择下拉框
        /// </summary>
        public List<string> WindowTitles { get; set; } = new List<string>();

        /// <summary>
        /// 重置所有捕获相关状态到初始值
        /// 在停止录制后调用，准备下一次录制
        /// </summary>
        public void ResetCaptureState()
        {
            Running = false;
            FrameNumber = 0;
            SavedCount = 0;
            FirstStart = false;
            Hwnd = nint.Zero;
            OutputFolder = "";
            VideoPath = null;
            
            // 释放上一帧图像
            if (LastImage != null)
            {
                LastImage.Dispose();
                LastImage = null;
            }
            
            // 释放视频写入器
            if (VideoWriter != null)
            {
                VideoWriter.Release();
                VideoWriter.Dispose();
                VideoWriter = null;
            }
        }
    }
}
