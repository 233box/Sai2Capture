namespace Sai2Capture.Models
{
    /// <summary>
    /// 录制帧数据 - 记录每帧的结构化信息（用于二进制存储）
    /// </summary>
    public class RecordingFrame
    {
        /// <summary>
        /// 帧序号
        /// </summary>
        public int FrameIndex { get; set; }

        /// <summary>
        /// 时间戳（毫秒，相对于录制开始）
        /// </summary>
        public long TimestampMs { get; set; }

        /// <summary>
        /// 帧数据在文件中的偏移量
        /// </summary>
        public long DataOffset { get; set; }

        /// <summary>
        /// 帧数据长度（字节）
        /// </summary>
        public int DataLength { get; set; }

        /// <summary>
        /// 帧宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 帧高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 是否为关键帧（第一帧或场景变化帧）
        /// </summary>
        public bool IsKeyFrame { get; set; }

        /// <summary>
        /// 压缩质量（0-100，仅用于 JPEG）
        /// </summary>
        public int Quality { get; set; } = 85;
    }

    /// <summary>
    /// 录制会话元数据
    /// </summary>
    public class RecordingMetadata
    {
        /// <summary>
        /// 文件魔数 "SAI2REC01"
        /// </summary>
        public string MagicNumber { get; set; } = "SAI2REC01";

        /// <summary>
        /// 录制开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 录制结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 窗口标题
        /// </summary>
        public string WindowTitle { get; set; } = string.Empty;

        /// <summary>
        /// 窗口句柄
        /// </summary>
        public long WindowHandle { get; set; }

        /// <summary>
        /// 捕获间隔（秒）
        /// </summary>
        public double CaptureInterval { get; set; }

        /// <summary>
        /// 画布宽度
        /// </summary>
        public int CanvasWidth { get; set; }

        /// <summary>
        /// 画布高度
        /// </summary>
        public int CanvasHeight { get; set; }

        /// <summary>
        /// 总帧数
        /// </summary>
        public int TotalFrames { get; set; }

        /// <summary>
        /// 有效帧数（非空帧）
        /// </summary>
        public int ValidFrames { get; set; }

        /// <summary>
        /// 软件版本
        /// </summary>
        public string SoftwareVersion { get; set; } = "1.0.0";

        /// <summary>
        /// 压缩质量（0-100）
        /// </summary>
        public int Quality { get; set; } = 85;

        /// <summary>
        /// 元数据在文件中的偏移量（用于更新）
        /// </summary>
        public long MetadataOffset { get; set; }

        /// <summary>
        /// 帧列表
        /// </summary>
        public List<RecordingFrame> Frames { get; set; } = new();
    }

    /// <summary>
    /// 自定义录制文件格式头
    /// 文件结构：
    /// [文件头 16 字节][元数据 JSON][帧索引表][帧数据...]
    /// </summary>
    public static class RecordingFileFormat
    {
        /// <summary>
        /// 文件魔数长度
        /// </summary>
        public const int MagicLength = 9; // "SAI2REC01"

        /// <summary>
        /// 文件头版本偏移
        /// </summary>
        public const int VersionOffset = 9;

        /// <summary>
        /// 元数据偏移量偏移
        /// </summary>
        public const int MetadataOffsetOffset = 13;

        /// <summary>
        /// 帧数量偏移
        /// </summary>
        public const int FrameCountOffset = 21;

        /// <summary>
        /// 文件头总长度
        /// </summary>
        public const int HeaderLength = 29;
    }
}
