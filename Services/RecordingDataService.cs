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

        // JPEG 编码参数
        private readonly int _jpegQuality = 85;

        public RecordingDataService(LogService logService, SettingsService settingsService)
        {
            _logService = logService;
            _settingsService = settingsService;
        }

        /// <summary>
        /// 开始新的录制会话
        /// </summary>
        public void StartRecording(string windowTitle, nint windowHandle, double captureInterval, int canvasWidth, int canvasHeight)
        {
            lock (_lock)
            {
                _recordingStartTime = DateTime.Now;
                var recordingDirectory = _settingsService.SavePath;

                if (!Directory.Exists(recordingDirectory))
                {
                    Directory.CreateDirectory(recordingDirectory);
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

                    var frameEntry = new RecordingFrame
                    {
                        FrameIndex = frameIndex,
                        TimestampMs = timestampMs,
                        DataOffset = dataOffset,
                        DataLength = frameData.Length,
                        Width = frame.Width,
                        Height = frame.Height,
                        IsKeyFrame = isKeyFrame || frameIndex == 0,
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
        /// 从录制文件导出为视频
        /// </summary>
        /// <param name="recordingFilePath">.sai2rec 文件路径</param>
        /// <param name="outputVideoPath">输出视频路径</param>
        /// <param name="fps">帧率（可选，默认根据录制间隔计算）</param>
        /// <returns>是否成功</returns>
        public bool ExportToVideo(string recordingFilePath, string outputVideoPath, double? fps = null)
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
                double actualFps = fps ?? (1.0 / metadata.CaptureInterval);
                if (actualFps <= 0) actualFps = 20;

                var width = metadata.CanvasWidth > 0 ? metadata.CanvasWidth : metadata.Frames[0].Width;
                var height = metadata.CanvasHeight > 0 ? metadata.CanvasHeight : metadata.Frames[0].Height;

                _logService.AddLog($"导出参数：{metadata.Frames.Count} 帧，{actualFps:F2} FPS, 尺寸：{width}x{height}");

                // 创建视频写入器
                using var videoWriter = new VideoWriter();
                var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');
                var size = new OpenCvSharp.Size(width, height);

                videoWriter.Open(outputVideoPath, fourcc, actualFps, size);

                if (!videoWriter.IsOpened())
                {
                    _logService.AddLog("无法打开视频写入器", LogLevel.Error);
                    return false;
                }

                // 使用文件流读取帧数据
                using var fileStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fileStream);

                // 按顺序写入帧
                int frameCount = 0;
                var sortedFrames = metadata.Frames.OrderBy(f => f.FrameIndex).ToList();

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
                            Cv2.Resize(frame, frame, size);
                        }

                        videoWriter.Write(frame);
                        frameCount++;

                        if (frameCount % 100 == 0)
                        {
                            _logService.AddLog($"导出进度：{frameCount}/{sortedFrames.Count} 帧");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService.AddLog($"读取帧 #{frameData.FrameIndex} 失败：{ex.Message}", LogLevel.Warning);
                    }
                }

                videoWriter.Release();

                _logService.AddLog($"✓ 视频导出成功 - {outputVideoPath}");
                _logService.AddLog($"  总帧数：{frameCount}, 文件大小：{new FileInfo(outputVideoPath).Length / 1024.0:F2} KB");

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

                // 验证文件魔数
                using var verifyStream = new FileStream(recordingFilePath, FileMode.Open, FileAccess.Read);
                var magicBuffer = new byte[RecordingFileFormat.MagicLength];
                verifyStream.Read(magicBuffer, 0, RecordingFileFormat.MagicLength);
                var magicNumber = Encoding.ASCII.GetString(magicBuffer);

                if (magicNumber != "SAI2REC01")
                {
                    _logService.AddLog($"无效的文件格式：{magicNumber}", LogLevel.Error);
                    return null;
                }

                // 读取元数据偏移量
                verifyStream.Position = RecordingFileFormat.MetadataOffsetOffset;
                using var reader = new BinaryReader(verifyStream);
                var metadataOffset = reader.ReadInt64();

                // 读取元数据
                verifyStream.Position = metadataOffset;
                var metadataLength = reader.ReadInt32();
                var metadataBytes = reader.ReadBytes(metadataLength);
                var metadataJson = Encoding.UTF8.GetString(metadataBytes);

                return JsonSerializer.Deserialize<RecordingMetadata>(metadataJson);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载元数据失败：{ex.Message}", LogLevel.Error);
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
