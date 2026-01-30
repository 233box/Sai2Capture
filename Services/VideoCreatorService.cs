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
    public partial class VideoCreatorService : ObservableObject
    {
        private readonly SharedStateService _sharedState;
        private readonly Dispatcher _dispatcher;

        [ObservableProperty]
        private string _status = "准备就绪";

        [ObservableProperty]
        private double _progress;

        public VideoCreatorService(SharedStateService sharedState, Dispatcher dispatcher)
        {
            _sharedState = sharedState;
            _dispatcher = dispatcher;
        }

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

        private void UpdateStatus(string message)
        {
            _dispatcher.Invoke(() =>
            {
                Status = message;
            });
        }

        private void UpdateProgress(double value)
        {
            _dispatcher.Invoke(() =>
            {
                Progress = value;
            });
        }
    }
}
