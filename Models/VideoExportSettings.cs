namespace Sai2Capture.Models
{
    /// <summary>
    /// 视频导出配置
    /// </summary>
    public class VideoExportSettings
    {
        /// <summary>
        /// 帧率（FPS）
        /// </summary>
        public double Fps { get; set; } = 20;

        /// <summary>
        /// 输出宽度（0 表示使用原始分辨率）
        /// </summary>
        public int OutputWidth { get; set; } = 0;

        /// <summary>
        /// 输出高度（0 表示使用原始分辨率）
        /// </summary>
        public int OutputHeight { get; set; } = 0;

        /// <summary>
        /// 视频编解码器
        /// </summary>
        public VideoCodec Codec { get; set; } = VideoCodec.H264;

        /// <summary>
        /// 视频质量（1-100，仅用于某些编解码器）
        /// </summary>
        public int Quality { get; set; } = 80;

        /// <summary>
        /// 是否保持原始分辨率
        /// </summary>
        public bool KeepOriginalResolution => OutputWidth == 0 && OutputHeight == 0;

        /// <summary>
        /// 常见的预定义分辨率
        /// </summary>
        public static readonly (int Width, int Height)[] CommonResolutions = new[]
        {
            (640, 480),    // VGA
            (1280, 720),   // 720p
            (1920, 1080),  // 1080p
            (2560, 1440),  // 2K
            (3840, 2160),  // 4K
        };
    }

    /// <summary>
    /// 视频编解码器类型
    /// </summary>
    public enum VideoCodec
    {
        /// <summary>
        /// H.264 / AVC（最通用，推荐）
        /// </summary>
        H264,

        /// <summary>
        /// MPEG-4（兼容性较好）
        /// </summary>
        MPEG4,

        /// <summary>
        /// Motion JPEG（每帧独立 JPEG 压缩）
        /// </summary>
        MJPEG,

        /// <summary>
        /// 未压缩（文件大，质量最高）
        /// </summary>
        Raw
    }

    /// <summary>
    /// 编解码器扩展方法
    /// </summary>
    public static class VideoCodecExtensions
    {
        /// <summary>
        /// 获取编解码器的 FourCC 码
        /// </summary>
        public static int GetFourCC(this VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.H264 => OpenCvSharp.VideoWriter.FourCC('H', '2', '6', '4'),
                VideoCodec.MPEG4 => OpenCvSharp.VideoWriter.FourCC('m', 'p', '4', 'v'),
                VideoCodec.MJPEG => OpenCvSharp.VideoWriter.FourCC('M', 'J', 'P', 'G'),
                VideoCodec.Raw => OpenCvSharp.VideoWriter.FourCC('\0', '\0', '\0', '\0'),
                _ => OpenCvSharp.VideoWriter.FourCC('H', '2', '6', '4')
            };
        }

        /// <summary>
        /// 获取编解码器的文件扩展名
        /// </summary>
        public static string GetFileExtension(this VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.H264 => ".mp4",
                VideoCodec.MPEG4 => ".mp4",
                VideoCodec.MJPEG => ".avi",
                VideoCodec.Raw => ".avi",
                _ => ".mp4"
            };
        }

        /// <summary>
        /// 获取编解码器的描述
        /// </summary>
        public static string GetDescription(this VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.H264 => "H.264 / AVC（推荐，最通用）",
                VideoCodec.MPEG4 => "MPEG-4（兼容性较好）",
                VideoCodec.MJPEG => "Motion JPEG（编辑友好）",
                VideoCodec.Raw => "未压缩（文件很大）",
                _ => "未知"
            };
        }
    }
}
