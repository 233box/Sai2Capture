using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// SAI2 文件格式解析器
    /// .sai2 文件格式头：SAI-CANVAS-TYPE0
    /// </summary>
    public static class Sai2FileParser
    {
        /// <summary>
        /// SAI2 文件头魔术数
        /// </summary>
        private static readonly byte[] SAI2_HEADER = Encoding.ASCII.GetBytes("SAI-CANVAS-TYPE0");

        /// <summary>
        /// 解析 SAI2 文件，获取画布尺寸
        /// </summary>
        /// <param name="filePath">.sai2 文件路径</param>
        /// <param name="width">输出：画布宽度</param>
        /// <param name="height">输出：画布高度</param>
        /// <returns>是否解析成功</returns>
        public static bool TryParseCanvasSize(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 64)
                    {
                        return false;
                    }

                    var header = new byte[16];
                    fs.Read(header, 0, 16);

                    // 验证文件头 "SAI-CANVAS-TYPE0"
                    if (!header.AsSpan().SequenceEqual(SAI2_HEADER.AsSpan()))
                    {
                        return false;
                    }

                    // 读取画布尺寸
                    // 根据实际分析，偏移 0x14 是宽度，0x18 是高度
                    fs.Seek(0x14, SeekOrigin.Begin);
                    
                    var widthBytes = new byte[4];
                    var heightBytes = new byte[4];
                    
                    fs.Read(widthBytes, 0, 4);
                    fs.Read(heightBytes, 0, 4);
                    
                    width = BitConverter.ToInt32(widthBytes, 0);
                    height = BitConverter.ToInt32(heightBytes, 0);

                    // 验证尺寸是否合理
                    if (IsValidCanvasSize(width, height))
                    {
                        return true;
                    }

                    // 如果无效，尝试交换
                    var temp = width;
                    width = height;
                    height = temp;

                    if (IsValidCanvasSize(width, height))
                    {
                        return true;
                    }

                    width = 0;
                    height = 0;
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 查找系统中正在编辑的 SAI2 文件
        /// </summary>
        /// <returns>SAI2 文件路径列表</returns>
        public static string[] FindOpenSai2Files()
        {
            var result = new System.Collections.Generic.List<string>();

            try
            {
                // 方法 1: 通过进程打开的文件句柄查找（需要管理员权限）
                // 这需要使用 Windows API，比较复杂

                // 方法 2: 监控最近访问的 .sai2 文件
                var sai2Processes = Process.GetProcessesByName("sai2");
                if (sai2Processes.Length > 0)
                {
                    // 尝试从进程的工作目录或命令行参数中查找
                    foreach (var proc in sai2Processes)
                    {
                        try
                        {
                            // 获取主窗口标题（通常包含文件名）
                            var title = proc.MainWindowTitle;
                            if (!string.IsNullOrEmpty(title) && title.EndsWith(".sai2", StringComparison.OrdinalIgnoreCase))
                            {
                                // 从标题中提取文件路径
                                var path = ExtractFilePathFromWindowTitle(title);
                                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                {
                                    result.Add(path);
                                }
                            }
                        }
                        catch
                        {
                            // 忽略
                        }
                    }
                }
            }
            catch
            {
                // 忽略
            }

            return result.ToArray();
        }

        /// <summary>
        /// 从窗口标题提取文件路径
        /// </summary>
        private static string? ExtractFilePathFromWindowTitle(string title)
        {
            // 尝试匹配常见模式
            // "PaintTool SAI Ver.2 - C:\path\to\file.sai2"
            // "PaintTool SAI Ver.2 (64bit) Preview.2024.02.22 - D:\path\file.sai2"
            
            var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var potentialPath = parts[parts.Length - 1].Trim();
                if (potentialPath.EndsWith(".sai2", StringComparison.OrdinalIgnoreCase) && File.Exists(potentialPath))
                {
                    return potentialPath;
                }
            }

            return null;
        }

        /// <summary>
        /// 验证是否是有效的画布尺寸
        /// </summary>
        private static bool IsValidCanvasSize(int width, int height)
        {
            // SAI2 支持的画布尺寸范围
            const int MIN_SIZE = 16;
            const int MAX_SIZE = 32000; // SAI2 最大支持 32000x32000

            return width >= MIN_SIZE && width <= MAX_SIZE &&
                   height >= MIN_SIZE && height <= MAX_SIZE;
        }
    }
}
