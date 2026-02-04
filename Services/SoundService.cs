using System;
using System.IO;
using System.Media;
using System.Reflection;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 音效播放服务
    /// 负责播放嵌入资源中的wav音效文件
    /// </summary>
    public class SoundService : IDisposable
    {
        private readonly SoundPlayer? _soundPlayer;
        private readonly Assembly _assembly;

        public SoundService()
        {
            _assembly = Assembly.GetExecutingAssembly();
            _soundPlayer = new SoundPlayer();
        }

        /// <summary>
        /// 播放指定名称的音效
        /// </summary>
        /// <param name="soundName">音效名称（不含文件扩展名）</param>
        public void PlaySound(string soundName)
        {
            try
            {
                // 构建嵌入式资源路径
                var resourceName = $"Sai2Capture.Sounds.{soundName}.wav";

                // 检查资源是否存在
                var resourceStream = _assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    // 如果指定的wav文件不存在，尝试使用默认音效
                    resourceName = "Sai2Capture.Sounds.default_beep.wav";
                    resourceStream = _assembly.GetManifestResourceStream(resourceName);

                    if (resourceStream == null)
                    {
                        return; // 如果默认音效也不存在，直接返回
                    }
                }

                // 停止当前播放的声音
                _soundPlayer?.Stop();

                // 设置新的音效流
                _soundPlayer!.Stream = resourceStream;

                // 加载音频流
                _soundPlayer.Load();

                // 同步播放音效
                _soundPlayer.PlaySync();
            }
            catch (Exception ex)
            {
                // 静默处理异常，避免影响主要功能
                System.Diagnostics.Debug.WriteLine($"播放音效失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步播放音效（不阻塞UI）
        /// </summary>
        /// <param name="soundName">音效名称</param>
        public void PlaySoundAsync(string soundName)
        {
            System.Threading.Tasks.Task.Run(() => PlaySound(soundName));
        }

        /// <summary>
        /// 检查可用的嵌入音效资源
        /// </summary>
        public void ListAvailableSounds()
        {
            try
            {
                var sounds = new[]
                {
                    "start_capture", "pause_capture", "stop_capture",
                    "refresh_window_list", "preview_window",
                    "toggle_window_topmost", "export_log", "default_beep"
                };

                System.Diagnostics.Debug.WriteLine("=== 检查嵌入音效资源 ===");
                foreach (var sound in sounds)
                {
                    var resourceName = $"Sai2Capture.Sounds.{sound}.wav";
                    var stream = _assembly.GetManifestResourceStream(resourceName);
                    var exists = stream != null;
                    System.Diagnostics.Debug.WriteLine($"{sound}.wav: {(exists ? "存在" : "不存在")}");
                    stream?.Dispose();
                }
                System.Diagnostics.Debug.WriteLine("=== 检查完成 ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查音效资源失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _soundPlayer?.Dispose();
        }
    }
}