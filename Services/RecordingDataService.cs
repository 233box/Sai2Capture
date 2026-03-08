using System.IO;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using Sai2Capture.Models;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 录制数据服务 - 将录制内容写入自定义二进制格式文件，并提供导出为视频的功能
    /// 文件格式：.sai2rec（SAI2 Recording）
    /// 文件结构：[文件头 29 字节][元数据 JSON][帧索引表][帧数据...]
    /// 
    /// 容错机制：
    /// 1. 每 50 帧自动刷新到磁盘
    /// 2. 每 10 秒自动备份元数据
    /// 3. 支持从崩溃中恢复（通过读取已写入的帧索引）
    /// 4. 支持断点续录（加载现有录制文件继续录制）
    /// </summary>
    public class RecordingDataService
    {
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private RecordingMetadata? _currentRecording;
        private string? _recordingFilePath;
        private FileStream? _fileStream;
        private BinaryWriter? _writer;
        private readonly object _lock = new();
        private DateTime _recordingStartTime;
        private long _frameDataStartOffset;
        private readonly List<RecordingFrame> _frameIndex = new();
        private System.Windows.Threading.DispatcherTimer? _autoSaveTimer;
        private bool _isResuming = false;
        private int _resumeBaseFrameIndex = 0;

        // JPEG 编码参数
        private readonly int _jpegQuality = 85;
        
        // 自动保存间隔（秒）
        private const int AutoSaveIntervalSeconds = 10;

        public RecordingDataService(LogService logService, SettingsService settingsService)
        {
            _logService = logService;
            _settingsService = settingsService;
        }

        /// <summary>
        /// 开始新的录制会话
        /// </summary>
        public void StartRecording(string windowTitle, nint windowHandle, double captureInterval, int canvasWidth, int canvasHeight, string? resumeFromFilePath = null)
        {
            lock (_lock)
            {
                _recordingStartTime = DateTime.Now;
                var recordingDirectory = _settingsService.SavePath;

                if (!Directory.Exists(recordingDirectory))
                {
                    Directory.CreateDirectory(recordingDirectory);
                }

                // 如果是续录，使用原文件；否则创建新文件
                if (!string.IsNullOrEmpty(resumeFromFilePath) && File.Exists(resumeFromFilePath))
                {
                    ResumeRecording(resumeFromFilePath, windowTitle, windowHandle, captureInterval, canvasWidth, canvasHeight);
                    return;
                }

                var fileName = $"Recording_{_recordingStartTime:yyyyMMdd_HHmmss}.sai2rec";
                _recordingFilePath = Path.Combine(recordingDirectory, fileName);

                _logService.AddLog($"录制会话已启动 - 文件：{_recordingFilePath}");
                _logService.AddLog($"窗口：{windowTitle}, 尺寸：{canvasWidth}x{canvasHeight}, 间隔：{captureInterval}秒");

                // 创建录制文件
                _fileStream = new FileStream(_recordingFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                _writer = new BinaryWriter(_fileStream);

                // 写入文件头占位符（29 字节）
                // 魔数 "SAI2REC01" (9 字节) + 版本 (4 字节) + 元数据偏移 (8 字节) + 帧数量 (8 字节)
                _writer.Write(Encoding.ASCII.GetBytes("SAI2REC01"));
                _writer.Write(1); // 版本号
                _writer.Write(0L); // 元数据偏移（稍后更新）
                _writer.Write(0L); // 帧数量（稍后更新）

                _frameDataStartOffset = _fileStream.Position;

                _currentRecording = new RecordingMetadata
                {
                    StartTime = _recordingStartTime,
                    WindowTitle = windowTitle,
                    WindowHandle = windowHandle,
                    CaptureInterval = captureInterval,
                    CanvasWidth = canvasWidth,
                    CanvasHeight = canvasHeight,
                    SoftwareVersion = "1.0.0",
                    Quality = _jpegQuality
                };

                _frameIndex.Clear();
                _isResuming = false;
                _resumeBaseFrameIndex = 0;

                // 启动自动保存定时器
                StartAutoSaveTimer();
            }
        }

        /// <summary>
        /// 从现有录制文件恢复并继续录制
        /// </summary>
        private void ResumeRecording(string recordingFilePath, string windowTitle, nint windowHandle, double captureInterval, int canvasWidth, int canvasHeight)
        {
            try
            {
                _recordingFilePath = recordingFilePath;
                _isResuming = true;

                // 读取现有文件的元数据
                var existingMetadata = LoadMetadata(recordingFilePath);
                if (existingMetadata == null)
                {
                    _logService.AddLog($"无法读取录制文件元数据：{recordingFilePath}", LogLevel.Error);
                    throw new InvalidOperationException("无法读取录制文件元数据");
                }

                // 使用原文件的开始时间，保持时间连续性
                _recordingStartTime = existingMetadata.StartTime;
                _resumeBaseFrameIndex = existingMetadata.TotalFrames;

                _logService.AddLog($"从文件恢复录制：{recordingFilePath}");
                _logService.AddLog($"原录制信息：{existingMetadata.TotalFrames} 帧，开始时间：{existingMetadata.StartTime:yyyy-MM-dd HH:mm:ss}");

                // 以追加模式打开文件
                _fileStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                _writer = new BinaryWriter(_fileStream);

                // 读取现有帧索引
                _frameIndex.Clear();
                _frameIndex.AddRange(existingMetadata.Frames);

                // 定位到帧数据末尾（文件末尾）
                _fileStream.Seek(0, SeekOrigin.End);

                // 更新元数据
                _currentRecording = new RecordingMetadata
                {
                    StartTime = existingMetadata.StartTime,
                    WindowTitle = windowTitle,
                    WindowHandle = windowHandle,
                    CaptureInterval = captureInterval,
                    CanvasWidth = canvasWidth,
                    CanvasHeight = canvasHeight,
                    SoftwareVersion = "1.0.0",
                    Quality = _jpegQuality,
                    TotalFrames = existingMetadata.TotalFrames,
                    ValidFrames = existingMetadata.ValidFrames
                };

                _frameDataStartOffset = RecordingFileFormat.HeaderLength;

                _logService.AddLog($"续录模式：基础帧数 {_resumeBaseFrameIndex}，文件指针位置 {_fileStream.Position}");

                // 启动自动保存定时器
                StartAutoSaveTimer();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"恢复录制失败：{ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 启动自动保存定时器
        /// </summary>
        private void StartAutoSaveTimer()
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(AutoSaveIntervalSeconds)
            };
            _autoSaveTimer.Tick += (s, e) => AutoSaveMetadata();
            _autoSaveTimer.Start();
            _logService.AddLog($"自动保存定时器已启动（间隔：{AutoSaveIntervalSeconds}秒）");
        }

        /// <summary>
        /// 自动保存元数据到文件（用于崩溃恢复）
        /// </summary>
        private void AutoSaveMetadata()
        {
            lock (_lock)
            {
                if (_currentRecording == null || _writer == null || _fileStream == null)
                    return;

                try
                {
                    // 保存当前帧索引表的偏移量
                    var currentPos = _fileStream.Position;

                    // 写入临时元数据备份
                    var metadataBackupOffset = _fileStream.Position;
                    _currentRecording.EndTime = DateTime.Now;
                    _currentRecording.TotalFrames = _frameIndex.Count;
                    _currentRecording.ValidFrames = _frameIndex.Count(f => f.DataLength > 0);
                    _currentRecording.Frames = _frameIndex;
                    _currentRecording.MetadataOffset = metadataBackupOffset;

                    var metadataJson = JsonSerializer.Serialize(_currentRecording, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
                    var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

                    // 写入备份元数据长度和元数据
                    _writer.Write(metadataBytes.Length);
                    _writer.Write(metadataBytes);
                    _writer.Flush();

                    // 更新文件头中的元数据偏移和帧数量
                    _fileStream.Position = RecordingFileFormat.MetadataOffsetOffset;
                    _writer.Write(metadataBackupOffset);
                    _writer.Write((long)_frameIndex.Count);
                    _writer.Flush();

                    // 恢复文件指针位置
                    _fileStream.Position = currentPos;

                    _logService.AddLog($"自动保存元数据：{_frameIndex.Count} 帧，文件大小：{_fileStream.Length / 1024.0:F2} KB", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"自动保存元数据失败：{ex.Message}", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// 保存帧到录制文件（使用 JPEG 压缩）
        /// </summary>
        public void SaveFrame(Mat frame, int frameIndex, bool isKeyFrame = false)
        {
            lock (_lock)
            {
                if (_currentRecording == null || _writer == null || _fileStream == null)
                {
                    _logService.AddLog("录制会话未启动，无法保存帧", LogLevel.Error);
                    return;
                }

                if (frame == null || frame.Empty())
                {
                    _logService.AddLog($"帧 #{frameIndex} 为空，跳过保存", LogLevel.Warning);
                    return;
                }

                try
                {
                    // 使用 JPEG 压缩编码帧数据
                    byte[] frameData;
                    if (frame.Channels() == 4)
                    {
                        // 如果是 RGBA 格式，先转换为 BGR
                        using var bgr = new Mat();
                        Cv2.CvtColor(frame, bgr, ColorConversionCodes.RGBA2BGR);
                        Cv2.ImEncode(".jpg", bgr, out frameData);
                    }
                    else if (frame.Channels() == 1)
                    {
                        // 灰度图直接编码
                        Cv2.ImEncode(".jpg", frame, out frameData);
                    }
                    else
                    {
                        // BGR 格式直接编码
                        Cv2.ImEncode(".jpg", frame, out frameData);
                    }

                    // 记录帧索引信息
                    var dataOffset = _fileStream.Position;
                    var timestampMs = (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;

                    // 在续录模式下，帧索引需要加上基础帧数
                    var actualFrameIndex = _isResuming ? (_resumeBaseFrameIndex + _frameIndex.Count) : frameIndex;

                    var frameEntry = new RecordingFrame
                    {
                        FrameIndex = actualFrameIndex,
                        TimestampMs = timestampMs,
                        DataOffset = dataOffset,
                        DataLength = frameData.Length,
                        Width = frame.Width,
                        Height = frame.Height,
                        IsKeyFrame = isKeyFrame || _frameIndex.Count == 0,
                        Quality = _jpegQuality
                    };
                    _frameIndex.Add(frameEntry);

                    // 写入帧数据：[长度 4 字节][帧数据]
                    _writer.Write(frameData.Length);
                    _writer.Write(frameData);

                    _currentRecording.ValidFrames++;

                    // 每 50 帧刷新一次到磁盘
                    if (_currentRecording.ValidFrames % 50 == 0)
                    {
                        _writer.Flush();
                        _logService.AddLog($"已保存 {_currentRecording.ValidFrames} 帧，文件大小：{_fileStream.Position / 1024.0:F2} KB");
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"保存帧 #{frameIndex} 失败：{ex.Message}", LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// 停止录制并保存最终数据
        /// </summary>
        public string? StopRecording()
        {
            lock (_lock)
            {
                if (_currentRecording == null || _writer == null || _fileStream == null)
                {
                    _logService.AddLog("没有正在进行的录制会话", LogLevel.Warning);
                    return null;
                }

                try
                {
                    // 停止自动保存定时器
                    _autoSaveTimer?.Stop();
                    _autoSaveTimer = null;

                    _currentRecording.EndTime = DateTime.Now;
                    _currentRecording.TotalFrames = _frameIndex.Count;

                    // 记录帧索引表的位置
                    var frameIndexOffset = _fileStream.Position;

                    // 序列化元数据（包含帧索引）
                    _currentRecording.Frames = _frameIndex;
                    var metadataJson = JsonSerializer.Serialize(_currentRecording, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
                    var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

                    // 写入元数据长度和元数据
                    _writer.Write(metadataBytes.Length);
                    _writer.Write(metadataBytes);

                    // 更新文件头中的元数据偏移和帧数量
                    _fileStream.Position = RecordingFileFormat.MetadataOffsetOffset;
                    _writer.Write(frameIndexOffset);
                    _writer.Write((long)_frameIndex.Count);

                    // 刷新并关闭文件
                    _writer.Flush();
                    _fileStream.Position = 0;

                    var fileSize = _fileStream.Length;
                    _logService.AddLog($"录制会话已停止 - 总帧数：{_currentRecording.TotalFrames}");
                    _logService.AddLog($"录制文件：{_recordingFilePath}");
                    _logService.AddLog($"文件大小：{fileSize / 1024.0:F2} KB");

                    var filePath = _recordingFilePath;

                    // 清理资源
                    _writer.Close();
                    _writer.Dispose();
                    _fileStream.Close();
                    _fileStream.Dispose();
                    _writer = null;
                    _fileStream = null;
                    _currentRecording = null;
                    _recordingFilePath = null;

                    return filePath;
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"停止录制失败：{ex.Message}", LogLevel.Error);
                    _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);

                    // 尝试清理资源
                    try
                    {
                        _writer?.Close();
                        _fileStream?.Close();
                    }
                    catch { }

                    _writer = null;
                    _fileStream = null;
                    _currentRecording = null;
                    return null;
                }
            }
        }

        /// <summary>
        /// 从录制文件导出为视频（简化版本，使用默认配置）
        /// </summary>
        /// <param name="recordingFilePath">.sai2rec 文件路径</param>
        /// <param name="outputVideoPath">输出视频路径</param>
        /// <param name="fps">帧率（可选，默认根据录制间隔计算）</param>
        /// <returns>是否成功</returns>
        public bool ExportToVideo(string recordingFilePath, string outputVideoPath, double? fps = null)
        {
            var settings = new VideoExportSettings
            {
                Fps = fps ?? 20,
                Codec = VideoCodec.H264,
                OutputWidth = 0,
                OutputHeight = 0,
                Quality = 80
            };
            return ExportToVideo(recordingFilePath, outputVideoPath, settings);
        }

        /// <summary>
        /// 从录制文件导出为视频（支持完整配置）
        /// </summary>
        /// <param name="recordingFilePath">.sai2rec 文件路径</param>
        /// <param name="outputVideoPath">输出视频路径</param>
        /// <param name="settings">导出配置</param>
        /// <returns>是否成功</returns>
        public bool ExportToVideo(string recordingFilePath, string outputVideoPath, VideoExportSettings settings)
        {
            try
            {
                _logService.AddLog($"开始导出视频 - 源文件：{recordingFilePath}, 输出：{outputVideoPath}");

                if (!File.Exists(recordingFilePath))
                {
                    _logService.AddLog($"录制文件不存在：{recordingFilePath}", LogLevel.Error);
                    return false;
                }

                // 验证文件魔数
                using var verifyStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.Read);
                var magicBuffer = new byte[RecordingFileFormat.MagicLength];
                verifyStream.Read(magicBuffer, 0, RecordingFileFormat.MagicLength);
                var magicNumber = Encoding.ASCII.GetString(magicBuffer);

                if (magicNumber != "SAI2REC01")
                {
                    _logService.AddLog($"无效的文件格式：{magicNumber}", LogLevel.Error);
                    return false;
                }

                // 读取元数据
                var metadata = LoadMetadata(recordingFilePath);
                if (metadata == null || metadata.Frames.Count == 0)
                {
                    _logService.AddLog("录制文件无效或无帧数据", LogLevel.Error);
                    return false;
                }

                // 确定输出目录
                var outputDirectory = Path.GetDirectoryName(outputVideoPath);
                if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // 计算帧率
                double actualFps = settings.Fps > 0 ? settings.Fps : (1.0 / metadata.CaptureInterval);
                if (actualFps <= 0) actualFps = 20;

                // 计算输出尺寸
                int width = metadata.CanvasWidth > 0 ? metadata.CanvasWidth : metadata.Frames[0].Width;
                int height = metadata.CanvasHeight > 0 ? metadata.CanvasHeight : metadata.Frames[0].Height;

                // 如果配置了自定义分辨率，使用配置的分辨率
                if (settings.OutputWidth > 0 && settings.OutputHeight > 0)
                {
                    width = settings.OutputWidth;
                    height = settings.OutputHeight;
                }

                _logService.AddLog($"导出参数：{metadata.Frames.Count} 帧，{actualFps:F2} FPS, 尺寸：{width}x{height}, 编解码器：{settings.Codec}");

                // 创建视频写入器
                using var videoWriter = new VideoWriter();
                var fourcc = settings.Codec.GetFourCC();
                var size = new OpenCvSharp.Size(width, height);

                // 尝试打开视频写入器
                bool opened = videoWriter.Open(outputVideoPath, fourcc, actualFps, size);
                
                // 如果 H.264 打开失败，尝试回退到 MPEG4
                if (!opened && settings.Codec == VideoCodec.H264)
                {
                    _logService.AddLog("H.264 编解码器不可用，尝试回退到 MPEG4...", LogLevel.Warning);
                    fourcc = VideoCodec.MPEG4.GetFourCC();
                    opened = videoWriter.Open(outputVideoPath, fourcc, actualFps, size);
                }

                if (!opened)
                {
                    _logService.AddLog("无法打开视频写入器，可能缺少必要的编解码器", LogLevel.Error);
                    return false;
                }

                // 使用文件流读取帧数据
                using var fileStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fileStream);

                // 按顺序写入帧
                int frameCount = 0;
                var sortedFrames = metadata.Frames.OrderBy(f => f.FrameIndex).ToList();
                int totalFrames = sortedFrames.Count;

                foreach (var frameData in sortedFrames)
                {
                    try
                    {
                        // 读取帧数据
                        fileStream.Position = frameData.DataOffset;
                        var dataLength = reader.ReadInt32();
                        var jpegData = reader.ReadBytes(dataLength);

                        // 解码 JPEG 数据
                        using var frame = Cv2.ImDecode(jpegData, ImreadModes.Color);
                        if (frame == null || frame.Empty())
                        {
                            _logService.AddLog($"帧 #{frameData.FrameIndex} 解码失败", LogLevel.Warning);
                            continue;
                        }

                        // 调整尺寸以匹配输出尺寸（如果需要）
                        if (frame.Width != width || frame.Height != height)
                        {
                            Cv2.Resize(frame, frame, size, 0, 0, InterpolationFlags.Area);
                        }

                        videoWriter.Write(frame);
                        frameCount++;

                        // 更新进度
                        if (frameCount % 50 == 0 || frameCount == totalFrames)
                        {
                            double progress = (double)frameCount / totalFrames * 100;
                            _logService.AddLog($"导出进度：{frameCount}/{totalFrames} 帧 ({progress:F1}%)");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService.AddLog($"读取帧 #{frameData.FrameIndex} 失败：{ex.Message}", LogLevel.Warning);
                    }
                }

                videoWriter.Release();

                _logService.AddLog($"✓ 视频导出成功 - {outputVideoPath}");
                _logService.AddLog($"  总帧数：{frameCount}, 文件大小：{new FileInfo(outputVideoPath).Length / 1024.0 / 1024.0:F2} MB");

                return true;
            }
            catch (Exception ex)
            {
                _logService.AddLog($"导出视频失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 从录制文件加载元数据
        /// </summary>
        public RecordingMetadata? LoadMetadata(string recordingFilePath)
        {
            try
            {
                if (!File.Exists(recordingFilePath))
                {
                    _logService.AddLog($"录制文件不存在：{recordingFilePath}", LogLevel.Error);
                    return null;
                }

                var fileInfo = new FileInfo(recordingFilePath);
                _logService.AddLog($"加载元数据 - 文件：{recordingFilePath}, 大小：{fileInfo.Length} 字节", LogLevel.Info);

                // 验证文件魔数
                using var verifyStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.Read);
                var magicBuffer = new byte[RecordingFileFormat.MagicLength];
                verifyStream.Read(magicBuffer, 0, RecordingFileFormat.MagicLength);
                var magicNumber = Encoding.ASCII.GetString(magicBuffer);

                _logService.AddLog($"文件魔数：{magicNumber}", LogLevel.Info);

                if (magicNumber != "SAI2REC01")
                {
                    _logService.AddLog($"无效的文件格式：{magicNumber}", LogLevel.Error);
                    return null;
                }

                // 读取元数据偏移量
                verifyStream.Position = RecordingFileFormat.MetadataOffsetOffset;
                using var reader = new BinaryReader(verifyStream);
                var metadataOffset = reader.ReadInt64();

                _logService.AddLog($"元数据偏移量：{metadataOffset}, 文件大小：{fileInfo.Length}", LogLevel.Info);

                // 如果元数据偏移量为 0 或超出文件范围，尝试从文件末尾恢复
                if (metadataOffset <= 0 || metadataOffset >= fileInfo.Length)
                {
                    _logService.AddLog($"元数据偏移量无效，尝试从文件末尾恢复...", LogLevel.Warning);
                    return TryRecoverMetadata(recordingFilePath, verifyStream);
                }

                // 读取元数据
                verifyStream.Position = metadataOffset;
                var metadataLength = reader.ReadInt32();
                _logService.AddLog($"元数据长度：{metadataLength}", LogLevel.Info);

                if (metadataLength <= 0 || metadataOffset + 4 + metadataLength > fileInfo.Length)
                {
                    _logService.AddLog($"元数据长度无效，尝试从文件末尾恢复...", LogLevel.Warning);
                    return TryRecoverMetadata(recordingFilePath, verifyStream);
                }

                var metadataBytes = reader.ReadBytes(metadataLength);
                var metadataJson = Encoding.UTF8.GetString(metadataBytes);

                _logService.AddLog($"元数据 JSON 长度：{metadataJson.Length}", LogLevel.Info);

                return JsonSerializer.Deserialize<RecordingMetadata>(metadataJson);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载元数据失败：{ex.Message}", LogLevel.Error);
                _logService.AddLog($"异常堆栈：{ex.StackTrace}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// 尝试从文件末尾恢复元数据（用于崩溃恢复）
        /// </summary>
        private RecordingMetadata? TryRecoverMetadata(string recordingFilePath, FileStream fileStream)
        {
            try
            {
                // 从文件末尾向前搜索元数据
                // 元数据格式：[长度 4 字节][JSON 数据]
                var buffer = new byte[4];
                
                // 尝试从文件末尾读取元数据长度
                fileStream.Seek(-4, SeekOrigin.End);
                fileStream.Read(buffer, 0, 4);
                var metadataLength = BitConverter.ToInt32(buffer, 0);

                _logService.AddLog($"恢复模式：从文件末尾读取的元数据长度：{metadataLength}", LogLevel.Info);

                if (metadataLength <= 0 || metadataLength > fileStream.Length - 4)
                {
                    _logService.AddLog($"恢复失败：元数据长度无效", LogLevel.Error);
                    return null;
                }

                // 读取元数据
                fileStream.Seek(-(4 + metadataLength), SeekOrigin.End);
                var metadataBytes = new byte[metadataLength];
                fileStream.Read(metadataBytes, 0, metadataLength);
                var metadataJson = Encoding.UTF8.GetString(metadataBytes);

                _logService.AddLog($"恢复成功：元数据 JSON 长度：{metadataJson.Length}", LogLevel.Info);

                var metadata = JsonSerializer.Deserialize<RecordingMetadata>(metadataJson);
                if (metadata != null)
                {
                    _logService.AddLog($"恢复成功：总帧数 {metadata.TotalFrames}, 有效帧数 {metadata.ValidFrames}", LogLevel.Info);
                }

                return metadata;
            }
            catch (Exception ex)
            {
                _logService.AddLog($"恢复元数据失败：{ex.Message}", LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// 获取当前录制帧数
        /// </summary>
        public int GetCurrentFrameCount()
        {
            lock (_lock)
            {
                return _frameIndex.Count;
            }
        }

        /// <summary>
        /// 获取当前录制文件大小（KB）
        /// </summary>
        public long GetCurrentFileSizeKB()
        {
            lock (_lock)
            {
                if (_fileStream == null) return 0;
                return _fileStream.Position / 1024;
            }
        }

        /// <summary>
        /// 取消录制并删除文件
        /// </summary>
        public void CancelRecording()
        {
            lock (_lock)
            {
                try
                {
                    // 停止自动保存定时器
                    _autoSaveTimer?.Stop();
                    _autoSaveTimer = null;

                    _writer?.Close();
                    _fileStream?.Close();

                    if (!string.IsNullOrEmpty(_recordingFilePath) && File.Exists(_recordingFilePath))
                    {
                        File.Delete(_recordingFilePath);
                        _logService.AddLog("录制已取消，文件已删除");
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"取消录制失败：{ex.Message}", LogLevel.Error);
                }
                finally
                {
                    _writer = null;
                    _fileStream = null;
                    _currentRecording = null;
                    _recordingFilePath = null;
                    _frameIndex.Clear();
                }
            }
        }
    }
}
