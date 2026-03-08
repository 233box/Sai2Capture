using System.Diagnostics;
using System.IO;
using FFMpegCore;
using OpenCvSharp;
using ModelsVideoCodec = Sai2Capture.Models.VideoCodec;
using ModelsVideoExportSettings = Sai2Capture.Models.VideoExportSettings;

namespace Sai2Capture.Services
{
    /// <summary>
    /// FFmpeg 视频编码服务 - 使用 FFMpegCore 进行高质量视频导出
    /// 支持 H.264、H.265/HEVC、VP9、AV1 等现代编解码器
    /// FFmpeg 二进制文件已内置到项目中（通过 FFMPEG.STATIC 包）
    /// </summary>
    public class FFmpegVideoEncoder
    {
        private readonly LogService _logService;
        private bool _isCancelled = false;
        private Process? _ffmpegProcess;

        public FFmpegVideoEncoder(LogService logService)
        {
            _logService = logService;
            
            // 配置 FFmpeg 路径为应用程序目录（FFMPEG.STATIC 会复制到这里）
            GlobalFFOptions.Configure(new FFOptions
            {
                BinaryFolder = AppContext.BaseDirectory,
                TemporaryFilesFolder = Path.Combine(Path.GetTempPath(), "Sai2Capture_FFmpeg")
            });
        }

        /// <summary>
        /// 取消当前编码操作
        /// </summary>
        public void Cancel()
        {
            _isCancelled = true;
            try
            {
                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                    _logService.AddLog("[FFmpeg] 编码已取消", LogLevel.Warning);
                }
            }
            catch { }
        }

        /// <summary>
        /// 从帧序列导出视频（使用 FFmpeg 进程调用）
        /// </summary>
        public string? ExportVideo(
            List<(byte[] JpegData, int FrameIndex)> frames,
            string outputPath,
            ModelsVideoExportSettings settings,
            int width,
            int height,
            double fps,
            IProgress<double>? progress = null)
        {
            _isCancelled = false;

            try
            {
                _logService.AddLog($"[FFmpeg] 开始编码 - 输出：{outputPath}");
                _logService.AddLog($"[FFmpeg] 参数：{frames.Count} 帧，{fps:F2} FPS, 尺寸：{width}x{height}, 编解码器：{settings.Codec}");

                if (frames.Count == 0)
                {
                    _logService.AddLog("[FFmpeg] 帧列表为空", LogLevel.Error);
                    return null;
                }

                // 创建临时目录存放帧图片
                var tempDirectory = Path.Combine(Path.GetTempPath(), $"Sai2Capture_FFmpeg_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    // 解码并保存帧到临时目录
                    _logService.AddLog($"[FFmpeg] 正在解码 {frames.Count} 帧到临时目录...");
                    int decodedCount = 0;

                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (_isCancelled)
                        {
                            _logService.AddLog("[FFmpeg] 编码已取消", LogLevel.Warning);
                            return null;
                        }

                        var (jpegData, frameIndex) = frames[i];
                        try
                        {
                            using var decodedFrame = Cv2.ImDecode(jpegData, ImreadModes.Color);
                            if (decodedFrame == null || decodedFrame.Empty())
                            {
                                _logService.AddLog($"[FFmpeg] 帧 #{frameIndex} 解码失败，跳过", LogLevel.Warning);
                                continue;
                            }

                            // 调整为 BGR 格式
                            using var bgrFrame = decodedFrame.Channels() == 3
                                ? decodedFrame
                                : ConvertToBgr(decodedFrame);

                            // 调整尺寸到指定输出尺寸
                            using var resizedFrame = (bgrFrame.Width != width || bgrFrame.Height != height)
                                ? ResizeFrame(bgrFrame, width, height)
                                : bgrFrame;

                            // 保存为 PNG（无损，适合 FFmpeg 输入）
                            // 使用统一的文件名格式
                            var framePath = Path.Combine(tempDirectory, $"frame_{i:D6}.png");
                            
                            // 确保保存的尺寸正确
                            if (resizedFrame.Width != width || resizedFrame.Height != height)
                            {
                                _logService.AddLog($"[FFmpeg] 帧尺寸不匹配：{resizedFrame.Width}x{resizedFrame.Height}，期望：{width}x{height}", LogLevel.Warning);
                            }
                            
                            Cv2.ImEncode(".png", resizedFrame, out var pngData);
                            File.WriteAllBytes(framePath, pngData);
                            decodedCount++;

                            if (decodedCount % 50 == 0)
                            {
                                _logService.AddLog($"[FFmpeg] 已解码 {decodedCount}/{frames.Count} 帧");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logService.AddLog($"[FFmpeg] 处理帧 #{frameIndex} 失败：{ex.Message}", LogLevel.Warning);
                        }
                    }

                    if (decodedCount == 0)
                    {
                        _logService.AddLog("[FFmpeg] 没有成功解码的帧", LogLevel.Error);
                        return null;
                    }

                    _logService.AddLog($"[FFmpeg] 解码完成 - {decodedCount} 帧，开始编码...");

                    // 使用 FFmpeg 进程调用编码
                    var success = RunFFmpegProcess(
                        tempDirectory,
                        outputPath,
                        decodedCount,
                        fps,
                        width,
                        height,
                        settings,
                        progress);

                    if (success)
                    {
                        _logService.AddLog($"[FFmpeg] ✓ 编码成功 - {outputPath}");
                        var fileInfo = new FileInfo(outputPath);
                        _logService.AddLog($"[FFmpeg] 文件大小：{fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                        return outputPath;
                    }
                    else
                    {
                        _logService.AddLog("[FFmpeg] 编码失败", LogLevel.Error);
                        return null;
                    }
                }
                finally
                {
                    // 清理临时目录
                    try
                    {
                        if (Directory.Exists(tempDirectory))
                        {
                            Directory.Delete(tempDirectory, true);
                            _logService.AddLog("[FFmpeg] 临时文件已清理");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService.AddLog($"[FFmpeg] 清理临时文件失败：{ex.Message}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[FFmpeg] 编码失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"[FFmpeg] 堆栈跟踪：{ex.StackTrace}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// 运行 FFmpeg 进程进行编码
        /// </summary>
        private bool RunFFmpegProcess(
            string inputDirectory,
            string outputPath,
            int frameCount,
            double fps,
            int width,
            int height,
            ModelsVideoExportSettings settings,
            IProgress<double>? progress = null)
        {
            try
            {
                // 获取编解码器配置
                var codecConfig = GetCodecConfig(settings.Codec, settings.QualityLevel);

                _logService.AddLog($"[FFmpeg] 使用编解码器：{codecConfig.Name}, 预设：{codecConfig.Preset}, CRF: {codecConfig.CRF}");

                // 创建输入文件列表
                var inputPattern = Path.Combine(inputDirectory, "frame_%06d.png");

                // 验证第一帧是否存在
                var firstFramePath = Path.Combine(inputDirectory, "frame_000000.png");
                if (!File.Exists(firstFramePath))
                {
                    _logService.AddLog($"[FFmpeg] 第一帧文件不存在：{firstFramePath}", LogLevel.Error);
                    var files = Directory.GetFiles(inputDirectory, "*.png");
                    _logService.AddLog($"[FFmpeg] 目录中的 PNG 文件数：{files.Length}", LogLevel.Info);
                    if (files.Length > 0)
                    {
                        _logService.AddLog($"[FFmpeg] 第一个文件：{files[0]}", LogLevel.Info);
                    }
                    return false;
                }

                // 确保宽高是 2 的倍数（H.264 YUV420P 要求）
                int adjustedWidth = width % 2 == 0 ? width : width - 1;
                int adjustedHeight = height % 2 == 0 ? height : height - 1;
                
                if (adjustedWidth != width || adjustedHeight != height)
                {
                    _logService.AddLog($"[FFmpeg] 调整尺寸以符合 H.264 要求：{width}x{height} -> {adjustedWidth}x{adjustedHeight}", LogLevel.Warning);
                }

                // 构建 FFmpeg 命令参数
                // 使用 scale 滤镜确保输出尺寸是 2 的倍数
                // 因为帧已经在 OpenCvSharp 中调整到正确尺寸，这里只做微调
                var scaleFilter = $"scale={adjustedWidth}:{adjustedHeight}";
                var arguments = $"-f image2 -framerate {fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                                $"-i \"{inputPattern}\" " +
                                $"-c:v {codecConfig.Name} " +
                                $"-crf {codecConfig.CRF} " +
                                $"-preset {codecConfig.Preset} " +
                                $"{codecConfig.CustomArgs} " +
                                $"-vf \"{scaleFilter}\" " +
                                $"-pix_fmt yuv420p " +
                                $"-y \"{outputPath}\"";

                var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    _logService.AddLog($"[FFmpeg] 未找到 FFmpeg: {ffmpegPath}", LogLevel.Error);
                    _logService.AddLog("[FFmpeg] 请确保 FFMPEG.STATIC 包已正确安装", LogLevel.Error);
                    return false;
                }

                _logService.AddLog($"[FFmpeg] 输入目录：{inputDirectory}");
                _logService.AddLog($"[FFmpeg] 帧数：{frameCount}, FPS: {fps}");
                _logService.AddLog($"[FFmpeg] 命令：ffmpeg {arguments}");

                // 启动 FFmpeg 进程
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = AppContext.BaseDirectory
                };

                using var process = new Process { StartInfo = startInfo };
                _ffmpegProcess = process;

                // 用于跟踪进度
                var startTime = DateTime.Now;
                var lastProgressTime = DateTime.MinValue;
                var progressUpdateInterval = TimeSpan.FromMilliseconds(500);
                var errorOutput = new System.Text.StringBuilder();

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                        _logService.AddLog($"[FFmpeg] {e.Data}", LogLevel.Info);

                        // 尝试解析进度并回调
                        if (progress != null)
                        {
                            try
                            {
                                var now = DateTime.Now;
                                if ((now - lastProgressTime) >= progressUpdateInterval)
                                {
                                    var elapsed = now - startTime;
                                    var totalDuration = TimeSpan.FromSeconds(frameCount / fps);
                                    var percentage = Math.Min(elapsed.TotalSeconds / totalDuration.TotalSeconds * 100, 100);
                                    progress.Report(percentage);
                                    lastProgressTime = now;
                                }
                            }
                            catch { }
                        }
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                // 如果失败，记录详细错误信息
                if (process.ExitCode != 0)
                {
                    _logService.AddLog($"[FFmpeg] 标准错误输出：{errorOutput.ToString()}", LogLevel.Error);
                    _logService.AddLog($"[FFmpeg] 退出码：{process.ExitCode}", LogLevel.Error);
                }

                _ffmpegProcess = null;

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    _logService.AddLog("[FFmpeg] 编码完成", LogLevel.Info);
                    return true;
                }
                else
                {
                    _logService.AddLog($"[FFmpeg] 编码失败，退出码：{process.ExitCode}", LogLevel.Error);
                    if (!File.Exists(outputPath))
                    {
                        _logService.AddLog($"[FFmpeg] 输出文件未创建：{outputPath}", LogLevel.Error);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[FFmpeg] 编码异常：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"[FFmpeg] 堆栈：{ex.StackTrace}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 获取编解码器配置
        /// </summary>
        private (string Name, string Preset, int CRF, string CustomArgs) GetCodecConfig(ModelsVideoCodec codec, int qualityLevel)
        {
            // 质量等级映射到 CRF 值（1-5，1 为最高质量）
            int crf = qualityLevel switch
            {
                1 => 18,
                2 => 23,
                3 => 28,
                4 => 32,
                5 => 38,
                _ => 23
            };

            return codec switch
            {
                ModelsVideoCodec.H264 => ("libx264", "medium", crf, ""),
                ModelsVideoCodec.H265 => ("libx265", "medium", crf, "-x265-params log-level=error"),
                ModelsVideoCodec.VP9 => ("libvpx-vp9", "good", crf, ""),
                ModelsVideoCodec.AV1 => ("libaom-av1", "good", crf, "-aom-params log_level=error"),
                ModelsVideoCodec.MPEG4 => ("mpeg4", "medium", crf, ""),
                ModelsVideoCodec.MJPEG => ("mjpeg", "medium", 3, "-q:v 2"),
                ModelsVideoCodec.Raw => ("rawvideo", "ultrafast", 0, ""),
                _ => ("libx264", "medium", 23, "")
            };
        }

        /// <summary>
        /// 将图像转换为 BGR 格式
        /// </summary>
        private static Mat ConvertToBgr(Mat source)
        {
            if (source.Channels() == 3)
                return source;

            var bgr = new Mat();
            ColorConversionCodes code = source.Channels() switch
            {
                1 => ColorConversionCodes.GRAY2BGR,
                4 => ColorConversionCodes.BGRA2BGR,
                _ => ColorConversionCodes.BGRA2BGR
            };
            Cv2.CvtColor(source, bgr, code);
            return bgr;
        }

        /// <summary>
        /// 调整图像尺寸
        /// </summary>
        private static Mat ResizeFrame(Mat frame, int width, int height)
        {
            var resized = new Mat();
            Cv2.Resize(frame, resized, new OpenCvSharp.Size(width, height));
            return resized;
        }
    }

    /// <summary>
    /// VideoCodec 扩展方法 - FFmpeg 支持
    /// </summary>
    public static class FFmpegVideoCodecExtensions
    {
        /// <summary>
        /// 获取 FFmpeg 编解码器名称
        /// </summary>
        public static string GetFFmpegCodecName(this ModelsVideoCodec codec)
        {
            return codec switch
            {
                ModelsVideoCodec.H264 => "libx264",
                ModelsVideoCodec.H265 => "libx265",
                ModelsVideoCodec.VP9 => "libvpx-vp9",
                ModelsVideoCodec.AV1 => "libaom-av1",
                ModelsVideoCodec.MPEG4 => "mpeg4",
                ModelsVideoCodec.MJPEG => "mjpeg",
                ModelsVideoCodec.Raw => "rawvideo",
                _ => "libx264"
            };
        }

        /// <summary>
        /// 获取推荐的输出文件扩展名
        /// </summary>
        public static string GetRecommendedExtension(this ModelsVideoCodec codec)
        {
            return codec switch
            {
                ModelsVideoCodec.H264 => ".mp4",
                ModelsVideoCodec.H265 => ".mp4",
                ModelsVideoCodec.VP9 => ".webm",
                ModelsVideoCodec.AV1 => ".mp4",
                ModelsVideoCodec.MPEG4 => ".mp4",
                ModelsVideoCodec.MJPEG => ".avi",
                ModelsVideoCodec.Raw => ".avi",
                _ => ".mp4"
            };
        }
    }
}
