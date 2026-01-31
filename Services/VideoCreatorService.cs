using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 视频生成服务
    /// 负责将图像序列转换为视频文件
    /// 核心功能：
    /// 1. 浏览和选择输出文件夹
    /// 2. 计算合适帧率
    /// 3. 使用OpenCV生成MP4视频
    /// 4. 实时进度报告
    /// </summary>
    public partial class VideoCreatorService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly Dispatcher _dispatcher;

        /// <summary>
        /// 视频生成状态信息
        /// 可能取值示例：
        /// "准备就绪" - 初始状态
        /// "正在初始化视频生成..." - 准备阶段
        /// "正在生成视频: XX.XX%" - 生成中状态
        /// "视频已保存到 {路径}" - 完成状态
        /// "生成视频错误: {消息}" - 错误状态
        /// </summary>
        [ObservableProperty]
        private string _status = "准备就绪";

        /// <summary>
        /// 视频生成进度百分比(0-100)
        /// 实时反映当前生成进度
        /// </summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>
        /// 初始化视频生成服务
        /// </summary>
        /// <param name="sharedState">共享状态服务</param>
        /// <param name="dispatcher">UI线程调度器</param>
        public VideoCreatorService(SharedStateService sharedState, Dispatcher dispatcher)
        {
            _sharedState = sharedState;
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// 选择文件夹并创建视频
        /// 显示文件夹选择对话框，用户选择包含PNG图像的文件夹
        /// 成功后启动视频生成流程
        /// </summary>
        /// <param name="videoDuration">期望的视频时长(秒)</param>
        public void SelectFolderAndCreateVideo(double videoDuration)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择包含图片的文件夹",
                InitialDirectory = string.IsNullOrEmpty(_sharedState.OutputFolder) 
                    ? Directory.GetCurrentDirectory() 
                    : _sharedState.OutputFolder
            };

            if (dialog.ShowDialog() == true)
            {
                CreateVideoFromImages(dialog.FolderName, videoDuration);
            }
        }

        /// <summary>
        /// 从图像序列创建视频
        /// 1. 扫描指定文件夹中的PNG文件
        /// 2. 按帧序号排序图像
        /// 3. 计算合适的帧率
        /// 4. 启动后台线程生成视频
        /// </summary>
        /// <param name="folderPath">包含PNG图像的文件夹路径</param>
        /// <param name="videoDuration">期望的视频时长(秒)</param>
        /// <exception cref="Exception">处理过程中的异常会更新状态信息</exception>
        public void CreateVideoFromImages(string folderPath, double videoDuration)
        {
            try
            {
                var imageFiles = Directory.GetFiles(folderPath, "*.png")
                    .OrderBy(f => 
                    {
                        var fileName = Path.GetFileNameWithoutExtension(f);
                        return int.Parse(fileName.Split('_')[1]);
                    })
                    .ToArray();

                if (imageFiles.Length == 0)
                {
                    UpdateStatus("没有找到图片文件");
                    return;
                }

                if (videoDuration <= 0)
                {
                    UpdateStatus("视频时长必须大于0");
                    return;
                }

                string outputPath = Path.Combine(folderPath, "output.mp4");
                double fps = imageFiles.Length / videoDuration;

                var thread = new Thread(() => 
                {
                    GenerateVideo(imageFiles, outputPath, fps);
                })
                {
                    IsBackground = true
                };
                thread.Start();
            }
            catch (Exception ex)
            {
                UpdateStatus($"创建视频失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 核心视频生成方法
        /// 1. 初始化视频写入器(MP4V codec)
        /// 2. 遍历所有图像路径：
        ///   - 读取每帧图像
        ///   - 写入视频文件
        ///   - 更新进度状态
        /// 3. 处理中途取消请求
        /// 4. 释放视频写入器资源
        /// 5. 最终状态更新和清理
        /// </summary>
        /// <param name="imagePaths">有序图像路径数组</param>
        /// <param name="videoPath">输出视频文件路径</param>
        /// <param name="fps">计算得出的帧率(frames/second)</param>
        private void GenerateVideo(string[] imagePaths, string videoPath, double fps)
        {
            try
            {
                UpdateStatus("正在初始化视频生成...");

                using var firstImage = Cv2.ImRead(imagePaths[0]);
                var size = new OpenCvSharp.Size(firstImage.Width, firstImage.Height);

                using var videoWriter = new VideoWriter(videoPath, 
                    FourCC.FromString("mp4v"),
                    fps, size);

                if (!videoWriter.IsOpened())
                {
                    UpdateStatus("无法创建视频文件");
                    return;
                }

                int totalFrames = imagePaths.Length;
                for (int i = 0; i < totalFrames; i++)
                {
                    if (_sharedState.Running)
                    {
                        using var frame = Cv2.ImRead(imagePaths[i]);
                        videoWriter.Write(frame);

                        double currentProgress = (i + 1) / (double)totalFrames * 100;
                        UpdateProgress(currentProgress);
                        UpdateStatus($"正在生成视频: {currentProgress:F2}%");
                    }
                    else
                    {
                        UpdateStatus("视频生成已取消");
                        return;
                    }
                }

                videoWriter.Release();
                UpdateStatus($"视频已保存到 {videoPath}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"生成视频错误: {ex.Message}");
            }
            finally
            {
                UpdateProgress(0);
            }
        }

        /// <summary>
        /// 线程安全的状态更新方法
        /// 通过Dispatcher在UI线程更新状态信息
        /// </summary>
        /// <param name="message">要显示的状态消息</param>
        private void UpdateStatus(string message)
        {
            _dispatcher.Invoke(() =>
            {
                Status = message;
            });
        }

        /// <summary>
        /// 线程安全的进度更新方法
        /// 通过Dispatcher在UI线程更新进度值
        /// </summary>
        /// <param name="value">进度百分比(0-100)</param>
        private void UpdateProgress(double value)
        {
            _dispatcher.Invoke(() =>
            {
                Progress = value;
            });
        }
    }
}
