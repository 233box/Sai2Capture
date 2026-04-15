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
                    _logService.AddLog("[导出] 编码已取消", LogLevel.Warning);
                }
            }
            catch { }
        }

        /// <summary>
        /// FFmpeg 进度解析状态
        /// </summary>
        private class ProgressParseState
        {
            public DateTime StartTime { get; set; }
            public double LastProgress { get; set; }
            public DateTime LastReportTime { get; set; } = DateTime.MinValue;
        }

        /// <summary>
        /// 解析 FFmpeg 输出中的进度信息
        /// 格式示例: frame= 1234 fps= 45 q=28.0 size= 5123kB time=00:01:02.30 bitrate= 682.1kbits/s speed=1.23x
        /// </summary>
        private void ParseFFmpegProgress(string line, double totalDurationSeconds, ProgressParseState state, IProgress<double>? progress)
        {
            try
            {
                // 解析 time= 字段 (格式: HH:MM:SS.ms)
                var timeIndex = line.IndexOf("time=");
                if (timeIndex < 0) return;

                var timeStr = line.Substring(timeIndex + 5, 12);
                var parts = timeStr.Split(':');
                if (parts.Length != 3) return;

                var hours = double.Parse(parts[0]);
                var minutes = double.Parse(parts[1]);
                var seconds = double.Parse(parts[2].Split(' ')[0].Split('.')[0] + "." + parts[2].Split(' ')[0].Split('.')[1]);

                var currentSeconds = hours * 3600 + minutes * 60 + seconds;
                var currentProgress = (currentSeconds / totalDurationSeconds) * 100;

                // 只在进度变化 >=10% 时输出，避免日志刷屏
                var now = DateTime.Now;
                if (currentProgress - state.LastProgress >= 10.0)
                {
                    state.LastProgress = currentProgress;
                    state.LastReportTime = now;

                    // 计算实际进度（30-100%）
                    var actualProgress = 30 + currentProgress * 0.7;
                    progress?.Report(Math.Min(actualProgress, 99.9));

                    // 输出编码阶段进度
                    var elapsed = (now - state.StartTime).TotalSeconds;
                    var eta = currentProgress > 0 ? (elapsed / currentProgress * 100) - elapsed : 0;
                    _logService.AddLog($"[导出-编码] 进度：{actualProgress:F0}% (已用 {elapsed:F0}秒, 剩余约 {eta:F0}秒)");
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
                _logService.AddLog($"[导出] 开始编码 - 输出：{outputPath}");
                _logService.AddLog($"[导出] 参数：{frames.Count} 帧，{fps:F2} FPS, 尺寸：{width}x{height}, 编解码器：{settings.Codec}");

                if (frames.Count == 0)
                {
                    _logService.AddLog("[导出] 帧列表为空", LogLevel.Error);
                    return null;
                }

                // 创建临时目录存放帧图片
                var tempDirectory = Path.Combine(Path.GetTempPath(), $"Sai2Capture_FFmpeg_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    // 解码并保存帧到临时目录
                    _logService.AddLog($"[导出] 正在解码 {frames.Count} 帧到临时目录...");
                    int decodedCount = 0;

                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (_isCancelled)
                        {
                            _logService.AddLog("[导出] 编码已取消", LogLevel.Warning);
                            return null;
                        }

                        var (jpegData, frameIndex) = frames[i];
                        try
                        {
                            using var decodedFrame = Cv2.ImDecode(jpegData, ImreadModes.Color);
                            if (decodedFrame == null || decodedFrame.Empty())
                            {
                                _logService.AddLog($"[导出] 帧 #{frameIndex} 解码失败，跳过", LogLevel.Warning);
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
                                _logService.AddLog($"[导出] 帧尺寸不匹配：{resizedFrame.Width}x{resizedFrame.Height}，期望：{width}x{height}", LogLevel.Warning);
                            }
                            
                            Cv2.ImEncode(".png", resizedFrame, out var pngData);
                            File.WriteAllBytes(framePath, pngData);
                            decodedCount++;

                            if (decodedCount % 50 == 0)
                            {
                                _logService.AddLog($"[导出] 已解码 {decodedCount}/{frames.Count} 帧");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logService.AddLog($"[导出] 处理帧 #{frameIndex} 失败：{ex.Message}", LogLevel.Warning);
                        }
                    }

                    if (decodedCount == 0)
                    {
                        _logService.AddLog("[导出] 没有成功解码的帧", LogLevel.Error);
                        return null;
                    }

                    _logService.AddLog($"[导出] 解码完成 - {decodedCount} 帧，开始编码...");

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
                        _logService.AddLog($"[导出] ✓ 编码成功 - {outputPath}");
                        var fileInfo = new FileInfo(outputPath);
                        _logService.AddLog($"[导出] 文件大小：{fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                        return outputPath;
                    }
                    else
                    {
                        _logService.AddLog("[导出] 编码失败", LogLevel.Error);
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
                            _logService.AddLog("[导出] 临时文件已清理");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService.AddLog($"[导出] 清理临时文件失败：{ex.Message}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[导出] 编码失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"[导出] 堆栈跟踪：{ex.StackTrace}", LogLevel.Error);
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

                _logService.AddLog($"[导出] 使用编解码器：{codecConfig.Name}, 预设：{codecConfig.Preset}, CRF: {codecConfig.CRF}");

                // 创建输入文件列表
                var inputPattern = Path.Combine(inputDirectory, "frame_%06d.png");

                // 验证第一帧是否存在
                var firstFramePath = Path.Combine(inputDirectory, "frame_000000.png");
                if (!File.Exists(firstFramePath))
                {
                    _logService.AddLog($"[导出] 第一帧文件不存在：{firstFramePath}", LogLevel.Error);
                    var files = Directory.GetFiles(inputDirectory, "*.png");
                    _logService.AddLog($"[导出] 目录中的 PNG 文件数：{files.Length}", LogLevel.Info);
                    if (files.Length > 0)
                    {
                        _logService.AddLog($"[导出] 第一个文件：{files[0]}", LogLevel.Info);
                    }
                    return false;
                }

                // 确保宽高是 2 的倍数（H.264 YUV420P 要求）
                int adjustedWidth = width % 2 == 0 ? width : width - 1;
                int adjustedHeight = height % 2 == 0 ? height : height - 1;
                
                if (adjustedWidth != width || adjustedHeight != height)
                {
                    _logService.AddLog($"[导出] 调整尺寸以符合 H.264 要求：{width}x{height} -> {adjustedWidth}x{adjustedHeight}", LogLevel.Warning);
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
                    _logService.AddLog($"[导出] 未找到 FFmpeg: {ffmpegPath}", LogLevel.Error);
                    _logService.AddLog("[导出] 请确保 FFMPEG.STATIC 包已正确安装", LogLevel.Error);
                    return false;
                }

                _logService.AddLog($"[导出] 输入目录：{inputDirectory}");
                _logService.AddLog($"[导出] 帧数：{frameCount}, FPS: {fps}");
                _logService.AddLog($"[导出] 命令：ffmpeg {arguments}");

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

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
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

                // 如果失败，记录错误信息
                if (process.ExitCode != 0)
                {
                    _logService.AddLog($"[导出] ✗ FFmpeg 编码失败，退出码：{process.ExitCode}", LogLevel.Error);
                }

                _ffmpegProcess = null;

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    _logService.AddLog("[导出] 编码完成");
                    return true;
                }
                else
                {
                    _logService.AddLog($"[导出] ✗ 编码失败，退出码：{process.ExitCode}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[导出] ✗ 编码异常：{ex.Message}", LogLevel.Error);
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

        /// <summary>
        /// 快速导出：跳过 MJPEG，直接从 JPEG 转目标格式
        /// 流程：JPEG 写入临时文件 → FFmpeg 直接编码为 H.264/H.265
        /// </summary>
        public string? ExportVideoTwoStage(
            List<(byte[] JpegData, int FrameIndex, int Width, int Height)> frames,
            string outputPath,
            ModelsVideoExportSettings settings,
            double fps,
            IProgress<double>? progress = null)
        {
            _isCancelled = false;

            try
            {
                if (frames.Count == 0)
                {
                    _logService.AddLog("[导出] 帧列表为空", LogLevel.Error);
                    return null;
                }

                // 验证所有帧尺寸一致
                var firstWidth = frames[0].Width;
                var firstHeight = frames[0].Height;
                if (!frames.All(f => f.Width == firstWidth && f.Height == firstHeight))
                {
                    _logService.AddLog("[导出] 检测到帧尺寸不一致，跳过优化导出", LogLevel.Warning);
                    return null;
                }

                var width = firstWidth;
                var height = firstHeight;

                _logService.AddLog($"[导出] 快速导出开始 - {frames.Count} 帧, {fps:F2} FPS, 尺寸：{width}x{height}");
                _logService.AddLog($"[导出] 目标格式：{settings.Codec}, 质量等级：{settings.QualityLevel}");

                // MJPEG 格式直接用快速导出
                if (settings.Codec == ModelsVideoCodec.MJPEG)
                {
                    if (ExportToMJPEGFast(frames, outputPath, fps, width, height, progress))
                    {
                        progress?.Report(100);
                        _logService.AddLog($"[导出] ✓ 导出完成（MJPEG）- {outputPath}");
                        return outputPath;
                    }
                    return null;
                }

                // ===== 直接从 JPEG 转目标格式 =====
                var success = ExportDirectFromJPEG(frames, outputPath, settings, fps, width, height, progress);

                if (success)
                {
                    progress?.Report(100);
                    _logService.AddLog($"[导出] ✓ 导出完成 - {outputPath}");
                    var fileInfo = new FileInfo(outputPath);
                    _logService.AddLog($"[导出] 文件大小：{fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                    return outputPath;
                }
                else
                {
                    _logService.AddLog("[导出] 直接导出失败，回退到传统方法...", LogLevel.Warning);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[导出] 导出异常：{ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// 直接从 JPEG 文件转码为 H.264/H.265（跳过 MJPEG 中间步骤）
        /// </summary>
        private bool ExportDirectFromJPEG(
            List<(byte[] JpegData, int FrameIndex, int Width, int Height)> frames,
            string outputPath,
            ModelsVideoExportSettings settings,
            double fps,
            int width,
            int height,
            IProgress<double>? progress = null)
        {
            var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                _logService.AddLog("[导出] FFmpeg 未找到", LogLevel.Error);
                return false;
            }

            // 创建临时目录存储 JPEG 文件
            var tempDir = Path.Combine(Path.GetTempPath(), $"Sai2Capture_JPEG_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                _logService.AddLog($"[导出-写入] 保存 {frames.Count} 帧 JPEG...");

                // 阶段1：并行写入 JPEG 文件
                var lastReportTime = DateTime.Now;
                var lockObj = new object();
                int completed = 0;

                Parallel.For(0, frames.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    var framePath = Path.Combine(tempDir, $"frame_{i:D6}.jpg");
                    File.WriteAllBytes(framePath, frames[i].JpegData);

                    lock (lockObj)
                    {
                        completed++;
                        var now = DateTime.Now;
                        if (now - lastReportTime >= TimeSpan.FromMilliseconds(500))
                        {
                            var pct = (double)completed / frames.Count * 100;
                            progress?.Report(pct * 0.3);  // 0-30%
                            lastReportTime = now;
                        }
                    }
                });

                progress?.Report(30);
                _logService.AddLog("[导出-写入] 完成");

                // 阶段2：FFmpeg 直接编码 (30-100%)
                _logService.AddLog("[导出-编码] 开始编码...");

                var codecConfig = GetCodecConfig(settings.Codec, settings.QualityLevel);

                // 确保宽高是偶数
                int adjWidth = width % 2 == 0 ? width : width - 1;
                int adjHeight = height % 2 == 0 ? height : height - 1;
                var scaleFilter = adjWidth != width || adjHeight != height
                    ? $"scale={adjWidth}:{adjHeight}"
                    : "scale=trunc(iw/2)*2:trunc(ih/2)*2";

                var inputPattern = Path.Combine(tempDir, "frame_%06d.jpg");
                var arguments = $"-framerate {fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                             $"-i \"{inputPattern}\" " +
                             $"-c:v {codecConfig.Name} " +
                             $"-crf {codecConfig.CRF} " +
                             $"-preset {codecConfig.Preset} " +
                             $"{codecConfig.CustomArgs} " +
                             $"-vf \"{scaleFilter}\" " +
                             $"-pix_fmt yuv420p " +
                             $"-y \"{outputPath}\"";

                _logService.AddLog($"[导出] 编码命令：ffmpeg {arguments}");

                // 计算总时长（秒）
                var totalDurationSeconds = frames.Count / fps;

                // 进度解析状态
                var parseState = new ProgressParseState { StartTime = DateTime.Now };

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = new Process { StartInfo = startInfo };
                _ffmpegProcess = process;

                var errorOutput = new System.Text.StringBuilder();
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                        ParseFFmpegProgress(e.Data, totalDurationSeconds, parseState, progress);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                process.WaitForExit(600000); // 10分钟超时

                _ffmpegProcess = null;

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    progress?.Report(100);
                    _logService.AddLog("[导出] 编码完成");
                    return true;
                }
                else
                {
                    _logService.AddLog($"[导出] ✗ 编码失败，退出码：{process.ExitCode}", LogLevel.Error);
                    try { File.Delete(outputPath); } catch { }
                    return false;
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>
        /// 超快速 MJPEG 导出 - 使用 FFmpeg image2pipe 编码
        /// 将 JPEG 数据写入临时文件，通过 FFmpeg 快速编码为 MJPEG
        /// </summary>
        private bool ExportToMJPEGFast(
            List<(byte[] JpegData, int FrameIndex, int Width, int Height)> frames,
            string outputPath,
            double fps,
            int width,
            int height,
            IProgress<double>? progress = null)
        {
            try
            {
                _logService.AddLog($"[导出] 超快速 MJPEG 导出：{frames.Count} 帧, {fps:F2} FPS");

                var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    _logService.AddLog("[导出] FFmpeg 未找到", LogLevel.Error);
                    return ExportToMJPEGWithVideoWriter(frames, outputPath, fps, width, height, progress);
                }

                // 创建临时目录存储 JPEG 文件
                var tempDir = Path.Combine(Path.GetTempPath(), $"Sai2Capture_JPEG_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    _logService.AddLog($"[导出] 保存 {frames.Count} 帧 JPEG 到临时目录...");

                    // 批量写入 JPEG 文件（并行加速）
                    var lastReportTime = DateTime.Now;
                    var lockObj = new object();
                    int completed = 0;

                    Parallel.For(0, frames.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                    {
                        var framePath = Path.Combine(tempDir, $"frame_{i:D6}.jpg");
                        File.WriteAllBytes(framePath, frames[i].JpegData);

                        lock (lockObj)
                        {
                            completed++;
                            var now = DateTime.Now;
                            if (now - lastReportTime >= TimeSpan.FromMilliseconds(300))
                            {
                                progress?.Report((double)completed / frames.Count * 30);
                                lastReportTime = now;
                            }
                        }
                    });

                    progress?.Report(30);
                    _logService.AddLog($"[导出] JPEG 文件写入完成");

                    // 使用 FFmpeg 快速编码 MJPEG
                    var inputPattern = Path.Combine(tempDir, "frame_%06d.jpg");
                    var arguments = $"-framerate {fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                                   $"-i \"{inputPattern}\" " +
                                   $"-c:v mjpeg " +
                                   $"-q:v 2 " +
                                   $"-pix_fmt yuv420p " +
                                   $"-y \"{outputPath}\"";

                    _logService.AddLog($"[导出] 编码命令：ffmpeg {arguments}");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var process = new Process { StartInfo = startInfo };
                    _ffmpegProcess = process;

                    var errorOutput = new System.Text.StringBuilder();
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            errorOutput.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginErrorReadLine();
                    process.WaitForExit(300000);

                    _ffmpegProcess = null;
                    progress?.Report(100);

                    if (process.ExitCode == 0 && File.Exists(outputPath))
                    {
                        var fileSize = new FileInfo(outputPath).Length;
                        _logService.AddLog($"[导出] MJPEG 导出完成 - {frames.Count} 帧, 文件大小：{fileSize / 1024.0 / 1024.0:F2} MB");
                        return true;
                    }
                    else
                    {
                        _logService.AddLog($"[导出] FFmpeg MJPEG 失败，退出码：{process.ExitCode}", LogLevel.Warning);
                        _logService.AddLog($"[导出] 错误：{errorOutput}", LogLevel.Warning);
                        try { File.Delete(outputPath); } catch { }
                        return ExportToMJPEGWithVideoWriter(frames, outputPath, fps, width, height, progress);
                    }
                }
                finally
                {
                    // 清理临时文件
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[导出] MJPEG 导出异常：{ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 使用 VideoWriter 的回退方法
        /// </summary>
        private bool ExportToMJPEGWithVideoWriter(
            List<(byte[] JpegData, int FrameIndex, int Width, int Height)> frames,
            string outputPath,
            double fps,
            int width,
            int height,
            IProgress<double>? progress = null)
        {
            try
            {
                using var writer = new VideoWriter(outputPath, VideoWriter.FourCC('M', 'J', 'P', 'G'), fps, new OpenCvSharp.Size(width, height));

                if (!writer.IsOpened())
                {
                    _logService.AddLog("[导出] 无法创建 VideoWriter", LogLevel.Error);
                    return false;
                }

                var lastReportTime = DateTime.Now;

                for (int i = 0; i < frames.Count; i++)
                {
                    if (_isCancelled) return false;

                    try
                    {
                        using var frame = Cv2.ImDecode(frames[i].JpegData, ImreadModes.Color);
                        if (frame != null && !frame.Empty())
                        {
                            writer.Write(frame);
                        }
                    }
                    catch { }

                    var now = DateTime.Now;
                    if (now - lastReportTime >= TimeSpan.FromMilliseconds(200))
                    {
                        progress?.Report((double)i / frames.Count * 100);
                        lastReportTime = now;
                    }
                }

                writer.Release();
                progress?.Report(100);
                return File.Exists(outputPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// FFmpeg 转码（阶段2）- 从 MJPEG 转换为目标格式
        /// </summary>
        private bool ConvertVideoWithFFmpeg(
            string inputPath,
            string outputPath,
            (string Name, string Preset, int CRF, string CustomArgs) codecConfig,
            double fps,
            int width,
            int height,
            IProgress<double>? progress = null)
        {
            try
            {
                // 确保宽高是 2 的倍数
                int adjustedWidth = width % 2 == 0 ? width : width - 1;
                int adjustedHeight = height % 2 == 0 ? height : height - 1;

                var scaleFilter = adjustedWidth != width || adjustedHeight != height
                    ? $"scale={adjustedWidth}:{adjustedHeight}"
                    : "scale=trunc(iw/2)*2:trunc(ih/2)*2"; // 确保输出尺寸为偶数

                var arguments = $"-i \"{inputPath}\" " +
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
                    _logService.AddLog("[导出] FFmpeg 未找到", LogLevel.Error);
                    return false;
                }

                _logService.AddLog($"[导出] 转码命令：ffmpeg {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = new Process { StartInfo = startInfo };
                _ffmpegProcess = process;

                var errorOutput = new System.Text.StringBuilder();
                var lastProgressTime = DateTime.MinValue;

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);

                        if (progress != null && (DateTime.Now - lastProgressTime).TotalMilliseconds > 300)
                        {
                            // 简单进度估算
                            progress.Report(50); // 实际 FFmpeg 会自己显示进度
                            lastProgressTime = DateTime.Now;
                        }
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                _ffmpegProcess = null;

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    _logService.AddLog("[导出] 转码完成");
                    return true;
                }
                else
                {
                    _logService.AddLog($"[导出] ✗ 转码失败，退出码：{process.ExitCode}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[导出] ✗ 转码异常：{ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 快速预览模式：只导出为 MJPEG，返回中间文件路径
        /// </summary>
        public string? ExportToMJPEGPreview(
            List<(byte[] JpegData, int FrameIndex, int Width, int Height)> frames,
            string? outputPath = null,
            double fps = 20,
            IProgress<double>? progress = null)
        {
            if (frames.Count == 0) return null;

            outputPath ??= Path.Combine(Path.GetTempPath(), $"Sai2Capture_Preview_{Guid.NewGuid()}.avi");

            var firstWidth = frames[0].Width;
            var firstHeight = frames[0].Height;

            if (ExportToMJPEGFast(frames, outputPath, fps, firstWidth, firstHeight, progress))
            {
                return outputPath;
            }
            return null;
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
