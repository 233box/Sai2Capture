using System.Media;
using System.Reflection;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 音效播放服务
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

        public void PlaySound(string soundName)
        {
            try
            {
                var resourceName = $"Sai2Capture.Sounds.{soundName}.wav";
                var resourceStream = _assembly.GetManifestResourceStream(resourceName);

                if (resourceStream == null)
                {
                    resourceName = "Sai2Capture.Sounds.default_beep.wav";
                    resourceStream = _assembly.GetManifestResourceStream(resourceName);
                    if (resourceStream == null) return;
                }

                _soundPlayer?.Stop();
                _soundPlayer!.Stream = resourceStream;
                _soundPlayer.Load();
                _soundPlayer.PlaySync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"播放音效失败：{ex.Message}");
            }
        }

        public void PlaySoundAsync(string soundName) => System.Threading.Tasks.Task.Run(() => PlaySound(soundName));

        public void ListAvailableSounds()
        {
            try
            {
                var sounds = new[] { "start_capture", "pause_capture", "stop_capture", "toggle_window_topmost", "default_beep" };
                System.Diagnostics.Debug.WriteLine("=== 检查嵌入音效资源 ===");
                foreach (var sound in sounds)
                {
                    var resourceName = $"Sai2Capture.Sounds.{sound}.wav";
                    var stream = _assembly.GetManifestResourceStream(resourceName);
                    System.Diagnostics.Debug.WriteLine($"{sound}.wav: {(stream != null ? "存在" : "不存在")}");
                    stream?.Dispose();
                }
                System.Diagnostics.Debug.WriteLine("=== 检查完成 ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查音效资源失败：{ex.Message}");
            }
        }

        public void Dispose() => _soundPlayer?.Dispose();
    }
}
