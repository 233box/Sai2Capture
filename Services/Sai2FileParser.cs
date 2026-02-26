using System;
using System.IO;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// SAI2 文件格式解析器 - 解析 .sai2 文件格式头 "SAI-CANVAS-TYPE0"
    /// </summary>
    public static class Sai2FileParser
    {
        private static readonly byte[] SAI2_HEADER = Encoding.ASCII.GetBytes("SAI-CANVAS-TYPE0");

        /// <summary>
        /// 解析 SAI2 文件，获取画布尺寸
        /// </summary>
        public static bool TryParseCanvasSize(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!File.Exists(filePath)) return false;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 64) return false;

                var header = new byte[16];
                fs.Read(header, 0, 16);

                if (!header.AsSpan().SequenceEqual(SAI2_HEADER.AsSpan())) return false;

                fs.Seek(0x14, SeekOrigin.Begin);
                var widthBytes = new byte[4];
                var heightBytes = new byte[4];
                fs.Read(widthBytes, 0, 4);
                fs.Read(heightBytes, 0, 4);

                width = BitConverter.ToInt32(widthBytes, 0);
                height = BitConverter.ToInt32(heightBytes, 0);

                if (IsValidCanvasSize(width, height)) return true;

                (width, height) = (height, width);
                return IsValidCanvasSize(width, height);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidCanvasSize(int width, int height)
        {
            const int MIN_SIZE = 16, MAX_SIZE = 32000;
            return width >= MIN_SIZE && width <= MAX_SIZE &&
                   height >= MIN_SIZE && height <= MAX_SIZE;
        }
    }
}
