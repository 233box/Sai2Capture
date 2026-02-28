using CommunityToolkit.Mvvm.ComponentModel;
using Sai2Capture.Models;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 全局热键管理服务
    /// </summary>
    public partial class HotkeyService : ObservableObject, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private readonly Dispatcher _dispatcher;
        private readonly SoundService _soundService;
        private IntPtr _windowHandle = IntPtr.Zero;
        private readonly Dictionary<int, HotkeyModel> _registeredHotkeys = new();
        private int _nextHotkeyId = 1000;
        private bool _isInitialized;

        public ObservableCollection<HotkeyModel> Hotkeys { get; private set; } = new();

        [ObservableProperty] private bool _hotkeysEnabled = true;

        public HotkeyService(LogService logService, SettingsService settingsService)
        {
            _logService = logService;
            _settingsService = settingsService;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _soundService = new SoundService();
            _soundService.ListAvailableSounds();
            _logService.AddLog("热键服务已创建");
        }

        public void Initialize(IntPtr windowHandle)
        {
            if (_isInitialized)
            {
                _logService.AddLog("热键服务已初始化，跳过重复初始化");
                return;
            }

            _windowHandle = windowHandle;
            if (_windowHandle == IntPtr.Zero) throw new ArgumentException("无效的窗口句柄");

            LoadHotkeys();
            RegisterAllHotkeys();
            _isInitialized = true;
            _logService.AddLog($"热键服务初始化完成，已注册 {_registeredHotkeys.Count} 个热键");
        }

        private void LoadHotkeys()
        {
            var defaultHotkeys = HotkeyModel.CreateDefaultHotkeys();
            Hotkeys.Clear();

            var savedHotkeys = _settingsService?.Hotkeys;
            if (savedHotkeys != null && savedHotkeys.Any())
            {
                foreach (var defaultHotkey in defaultHotkeys)
                {
                    var savedHotkey = savedHotkeys.FirstOrDefault(h => h.Id == defaultHotkey.Id);
                    if (savedHotkey != null)
                    {
                        defaultHotkey.CurrentKey = savedHotkey.CurrentKey ?? defaultHotkey.DefaultKey;
                        defaultHotkey.IsEnabled = savedHotkey.IsEnabled;
                    }
                    Hotkeys.Add(defaultHotkey);
                }
                _logService.AddLog($"加载了 {savedHotkeys.Count} 个热键配置");
            }
            else
            {
                foreach (var hotkey in defaultHotkeys)
                    Hotkeys.Add(hotkey);
                _logService.AddLog($"创建了 {defaultHotkeys.Count} 个默认热键配置");
            }
        }

        internal void RegisterAllHotkeys()
        {
            UnregisterAllHotkeys();
            foreach (var hotkey in Hotkeys.Where(h => h.IsEnabled))
                RegisterHotkey(hotkey);
        }

        private void RegisterHotkey(HotkeyModel hotkey)
        {
            try
            {
                var (keyCode, ctrl, alt, shift) = HotkeyModel.ParseKeyCombination(hotkey.CurrentKey);
                if (keyCode == 0)
                {
                    _logService.AddLog($"热键 '{hotkey.Name}' 组合无效：{hotkey.CurrentKey}", LogLevel.Warning);
                    return;
                }

                uint modifiers = 0x0000;
                if (ctrl) modifiers |= 0x0002;
                if (alt) modifiers |= 0x0001;
                if (shift) modifiers |= 0x0004;

                int hotkeyId = _nextHotkeyId++;
                bool success = RegisterHotKey(_windowHandle, hotkeyId, modifiers, (uint)keyCode);

                if (success)
                {
                    _registeredHotkeys[hotkeyId] = hotkey;
                    hotkey.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName is nameof(HotkeyModel.CurrentKey) or nameof(HotkeyModel.IsEnabled))
                            RefreshHotkey(hotkeyId);
                    };
                    _logService.AddLog($"注册热键成功：{hotkey.Name} = {hotkey.CurrentKey}");
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _logService.AddLog($"注册热键失败：{hotkey.Name} = {hotkey.CurrentKey}, 错误：{GetErrorMessage(errorCode)}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"注册热键异常：{hotkey.Name} - {ex.Message}", LogLevel.Error);
            }
        }

        private void RefreshHotkey(int hotkeyId)
        {
            if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkey))
            {
                UnregisterHotkey(hotkeyId);
                if (hotkey.IsEnabled) RegisterHotkey(hotkey);
            }
        }

        private void UnregisterAllHotkeys()
        {
            foreach (var hotkeyId in _registeredHotkeys.Keys.ToArray())
                UnregisterHotkey(hotkeyId);
        }

        private void UnregisterHotkey(int hotkeyId)
        {
            try
            {
                if (UnregisterHotKey(_windowHandle, hotkeyId))
                    _registeredHotkeys.Remove(hotkeyId);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"注销热键异常：{ex.Message}", LogLevel.Error);
            }
        }

        public bool ProcessWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (msg == WM_HOTKEY && HotkeysEnabled)
            {
                int hotkeyId = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkey))
                {
                    _dispatcher.BeginInvoke(() => TriggerHotkeyCommand(hotkey));
                    _logService.AddLog($"热键触发：{hotkey.Name}");
                    return true;
                }
            }
            return false;
        }

        private void TriggerHotkeyCommand(HotkeyModel hotkey)
        {
            if (!hotkey.IsEnabled) return;

            PlayHotkeyFeedback(hotkey.Id);
            OnHotkeyTriggered?.Invoke(this, new HotkeyEventArgs
            {
                HotkeyId = hotkey.Id,
                CommandName = hotkey.CommandName,
                Hotkey = hotkey
            });
            _logService.AddLog($"执行热键命令：{hotkey.Name} ({hotkey.CommandName})");
        }

        public void ResetToDefaults()
        {
            Hotkeys.Clear();
            foreach (var hotkey in HotkeyModel.CreateDefaultHotkeys())
                Hotkeys.Add(hotkey);
            if (_isInitialized) RegisterAllHotkeys();
            _logService.AddLog("热键已重置为默认配置");
        }

        public void UpdateHotkey(string id, string newKey)
        {
            var hotkey = Hotkeys.FirstOrDefault(h => h.Id == id);
            if (hotkey != null)
            {
                if (HotkeyModel.ValidateKeyCombination(newKey))
                {
                    hotkey.CurrentKey = newKey;
                    _logService.AddLog($"更新热键 {hotkey.Name}: {newKey}");
                }
                else
                {
                    _logService.AddLog($"无效的热键格式：{newKey}", LogLevel.Warning);
                }
            }
        }

        public void SaveHotkeys()
        {
            try
            {
                _settingsService?.SaveHotkeys(new ObservableCollection<HotkeyModel>(Hotkeys));
                _logService.AddLog("热键配置已保存");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键配置失败：{ex.Message}", LogLevel.Error);
            }
        }

        public event EventHandler<HotkeyEventArgs>? OnHotkeyTriggered;

        public void Dispose()
        {
            UnregisterAllHotkeys();
            _soundService?.Dispose();
            _logService.AddLog("热键服务已释放");
        }

        private void PlayHotkeyFeedback(string hotkeyId)
        {
            try
            {
                string soundFile = hotkeyId switch
                {
                    "start_capture" => "start_capture",
                    "pause_capture" => "pause_capture",
                    "stop_capture" => "stop_capture",
                    "toggle_window_topmost" => "toggle_window_topmost",
                    _ => "default_beep"
                };
                _soundService.PlaySoundAsync(soundFile);
            }
            catch (Exception ex)
            {
                _logService.AddLog($"播放热键反馈音效失败：{ex.Message}", LogLevel.Warning);
            }
        }

        private static string GetErrorMessage(int errorCode) => errorCode switch
        {
            1409 => "热键已注册 - 该热键已被其他应用程序注册",
            5 => "访问被拒绝 - 权限不足",
            87 => "参数错误 - 无效的修饰键或键码",
            1444 => "指定的窗口句柄无效",
            _ => $"未知错误代码：{errorCode}"
        };
    }

    public class HotkeyEventArgs : EventArgs
    {
        public string HotkeyId { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public HotkeyModel? Hotkey { get; set; }
    }
}
