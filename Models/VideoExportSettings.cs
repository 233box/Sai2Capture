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
        /// 视频编解码器
        /// </summary>
        public VideoCodec Codec { get; set; } = VideoCodec.MJPEG;

        /// <summary>
        /// 视频质量等级（1-5，1 为最高质量，5 为最小文件）
        /// </summary>
        public int QualityLevel { get; set; } = 2;

        /// <summary>
        /// 质量等级描述
        /// </summary>
        public string QualityDescription => QualityLevel switch
        {
            1 => "最高质量（文件最大）",
            2 => "高质量（推荐）",
            3 => "中等质量",
            4 => "较低质量",
            5 => "最低质量（文件最小）",
            _ => "高质量（推荐）"
        };

        /// <summary>
        /// 常见的预定义质量等级
        /// </summary>
        public static readonly (int Level, string Description)[] QualityLevels = new[]
        {
            (1, "最高质量（文件最大）"),
            (2, "高质量（推荐）"),
            (3, "中等质量"),
            (4, "较低质量"),
            (5, "最低质量（文件最小）"),
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
        /// H.265 / HEVC（高效压缩，适合 4K）
        /// </summary>
        H265,

        /// <summary>
        /// VP9（Google 开发，Web 友好）
        /// </summary>
        VP9,

        /// <summary>
        /// AV1（开源免专利，最新标准）
        /// </summary>
        AV1,

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
        /// 获取编解码器的 FourCC 码（用于 OpenCvSharp VideoWriter）
        /// </summary>
        public static int GetFourCC(this VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.H264 => OpenCvSharp.VideoWriter.FourCC('H', '2', '6', '4'),
                VideoCodec.H265 => OpenCvSharp.VideoWriter.FourCC('H', '2', '6', '5'),
                VideoCodec.VP9 => OpenCvSharp.VideoWriter.FourCC('V', 'P', '9', '0'),
                VideoCodec.AV1 => OpenCvSharp.VideoWriter.FourCC('A', 'V', '1', '0'),
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
                VideoCodec.H265 => ".mp4",
                VideoCodec.VP9 => ".webm",
                VideoCodec.AV1 => ".mp4",
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
                VideoCodec.H264 => "H.264 / AVC（推荐，兼容性最好）",
                VideoCodec.H265 => "H.265 / HEVC（高效压缩，适合 4K）",
                VideoCodec.VP9 => "VP9（Web 友好，适合网络播放）",
                VideoCodec.AV1 => "AV1（最新标准，开源免专利）",
                VideoCodec.MPEG4 => "MPEG-4（兼容性较好）",
                VideoCodec.MJPEG => "Motion JPEG（每帧独立，编辑友好）",
                VideoCodec.Raw => "未压缩（文件很大，质量最高）",
                _ => "未知编解码器"
            };
        }
    }
}
