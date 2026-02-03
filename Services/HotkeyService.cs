using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 全局热键管理服务
    /// 负责注册系统级全局热键，并转发到业务逻辑
    /// </summary>
    public partial class HotkeyService : ObservableObject, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private readonly Dispatcher _dispatcher;
        private IntPtr _windowHandle = IntPtr.Zero;
        private readonly Dictionary<int, HotkeyModel> _registeredHotkeys = new();
        private int _nextHotkeyId = 1000;
        private bool _isInitialized = false;

        /// <summary>
        /// 热键配置集合
        /// </summary>
        public ObservableCollection<HotkeyModel> Hotkeys { get; private set; }

        /// <summary>
        /// 热键已启用状态
        /// 允许用户临时禁用所有热键
        /// </summary>
        [ObservableProperty]
        private bool _hotkeysEnabled = true;

        /// <summary>
        /// 初始化热键服务
        /// </summary>
        /// <param name="logService">日志服务</param>
        /// <param name="settingsService">设置服务</param>
        public HotkeyService(LogService logService, SettingsService settingsService)
        {
            _logService = logService;
            _settingsService = settingsService;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Hotkeys = new ObservableCollection<HotkeyModel>();

            _logService.AddLog("热键服务初始化");
        }

        /// <summary>
        /// 传入主窗口句柄并初始化热键
        /// </summary>
        public void Initialize(IntPtr windowHandle)
        {
            try
            {
                if (_isInitialized)
                {
                    _logService.AddLog("热键服务已初始化，跳过重复初始化");
                    return;
                }

                _windowHandle = windowHandle;
                if (_windowHandle == IntPtr.Zero)
                {
                    throw new ArgumentException("无效的窗口句柄");
                }

                // 加载热键配置
                LoadHotkeys();

                // 注册所有热键
                RegisterAllHotkeys();

                _isInitialized = true;
                _logService.AddLog($"热键服务初始化完成，已注册 {_registeredHotkeys.Count} 个热键");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"热键服务初始化失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 加载热键配置
        /// </summary>
        private void LoadHotkeys()
        {
            try
            {
                // 首先加载默认热键
                var defaultHotkeys = HotkeyModel.CreateDefaultHotkeys();
                Hotkeys.Clear();

                // 检查是否有保存的自定义热键配置
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
                    _logService.AddLog($"加载了 {savedHotkeys.Count} 个已保存的热键配置");
                }
                else
                {
                    // 使用默认配置
                    foreach (var hotkey in defaultHotkeys)
                    {
                        Hotkeys.Add(hotkey);
                    }
                    _logService.AddLog("使用默认热键配置");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载热键配置失败: {ex.Message}", LogLevel.Error);
                // 失败时使用默认配置
                ResetToDefaults();
            }
        }

        /// <summary>
        /// 注册所有已启用的热键
        /// </summary>
        internal void RegisterAllHotkeys()
        {
            UnregisterAllHotkeys();

            foreach (var hotkey in Hotkeys.Where(h => h.IsEnabled))
            {
                RegisterHotkey(hotkey);
            }
        }

        /// <summary>
        /// 注册单个热键
        /// </summary>
        private void RegisterHotkey(HotkeyModel hotkey)
        {
            try
            {
                var (keyCode, ctrl, alt, shift) = HotkeyModel.ParseKeyCombination(hotkey.CurrentKey);
                if (keyCode == 0)
                {
                    _logService.AddLog($"热键 '{hotkey.Name}' 组合无效: {hotkey.CurrentKey}", LogLevel.Warning);
                    return;
                }

                uint modifiers = 0x0000;
                if (ctrl) modifiers |= 0x0002; // MOD_CONTROL
                if (alt) modifiers |= 0x0001; // MOD_ALT
                if (shift) modifiers |= 0x0004; // MOD_SHIFT
                // Windows键: 0x0008 MOD_WIN

                int hotkeyId = _nextHotkeyId++;
                bool success = RegisterHotKey(_windowHandle, hotkeyId, modifiers, (uint)keyCode);

                if (success)
                {
                    _registeredHotkeys[hotkeyId] = hotkey;
                    hotkey.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(HotkeyModel.CurrentKey) ||
                            e.PropertyName == nameof(HotkeyModel.IsEnabled))
                        {
                            RefreshHotkey(hotkeyId);
                        }
                    };
                    _logService.AddLog($"注册热键成功: {hotkey.Name} = {hotkey.CurrentKey}");
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = GetErrorMessage(errorCode);
                    _logService.AddLog($"注册热键失败: {hotkey.Name} = {hotkey.CurrentKey}, 错误代码: {errorCode}, 错误信息: {errorMessage}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"注册热键异常: {hotkey.Name} - {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 刷新热键注册状态
        /// </summary>
        private void RefreshHotkey(int hotkeyId)
        {
            if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkey))
            {
                UnregisterHotkey(hotkeyId);
                if (hotkey.IsEnabled)
                {
                    RegisterHotkey(hotkey);
                }
            }
        }

        /// <summary>
        /// 注销所有热键
        /// </summary>
        private void UnregisterAllHotkeys()
        {
            foreach (var hotkeyId in _registeredHotkeys.Keys.ToArray())
            {
                UnregisterHotkey(hotkeyId);
            }
        }

        /// <summary>
        /// 注销单个热键
        /// </summary>
        private void UnregisterHotkey(int hotkeyId)
        {
            try
            {
                if (UnregisterHotKey(_windowHandle, hotkeyId))
                {
                    _registeredHotkeys.Remove(hotkeyId);
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"注销热键异常: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 处理WndProc消息，接收热键触发事件
        /// </summary>
        public bool ProcessWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;

            if (msg == WM_HOTKEY && HotkeysEnabled)
            {
                int hotkeyId = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkey))
                {
                    _dispatcher.BeginInvoke(() =>
                    {
                        TriggerHotkeyCommand(hotkey);
                    });

                    _logService.AddLog($"热键触发: {hotkey.Name}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 触发热键关联的命令
        /// </summary>
        private void TriggerHotkeyCommand(HotkeyModel hotkey)
        {
            try
            {
                // 检查热键是否有效
                if (!hotkey.IsEnabled)
                {
                    return;
                }

                // 这里会根据CommandName在MainViewModel中查找对应的命令
                // 实际执行逻辑在MainViewModel中处理
                OnHotkeyTriggered?.Invoke(this, new HotkeyEventArgs
                {
                    HotkeyId = hotkey.Id,
                    CommandName = hotkey.CommandName,
                    Hotkey = hotkey
                });

                _logService.AddLog($"执行热键命令: {hotkey.Name} ({hotkey.CommandName})");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"执行热键命令失败: {hotkey.Name} - {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 重置所有热键为默认值
        /// </summary>
        public void ResetToDefaults()
        {
            var defaultHotkeys = HotkeyModel.CreateDefaultHotkeys();
            Hotkeys.Clear();

            foreach (var hotkey in defaultHotkeys)
            {
                Hotkeys.Add(hotkey);
            }

            RegisterAllHotkeys();
            _logService.AddLog("热键已重置为默认配置");
        }

        /// <summary>
        /// 更新热键配置
        /// </summary>
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
                    _logService.AddLog($"无效的热键格式: {newKey}", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// 保存热键配置
        /// </summary>
        public void SaveHotkeys()
        {
            try
            {
                if (_settingsService != null)
                {
                    _settingsService.SaveHotkeys(new ObservableCollection<HotkeyModel>(Hotkeys));
                    _logService.AddLog("热键配置已保存");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 热键触发事件
        /// </summary>
        public event EventHandler<HotkeyEventArgs>? OnHotkeyTriggered;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                UnregisterAllHotkeys();
                _logService.AddLog("热键服务已释放");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"释放热键服务异常: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 获取 Win32 错误信息
        /// </summary>
        private static string GetErrorMessage(int errorCode)
        {
            try
            {
                if (errorCode == 0) return "无错误";

                switch (errorCode)
                {
                    case 1409: return "热键已注册 (ERROR_HOTKEY_ALREADY_REGISTERED) - 该热键已被其他应用程序注册";
                    case 5: return "访问被拒绝 (ERROR_ACCESS_DENIED) - 权限不足";
                    case 1008: return "尝试引用不存在的令牌 (ERROR_NO_TOKEN)";
                    case 1155: return "没有应用程序与此操作的指定文件关联 (ERROR_NO_ASSOCIATION)";
                    case 2: return "系统找不到指定的文件 (ERROR_FILE_NOT_FOUND)";
                    case 3: return "系统找不到指定的路径 (ERROR_PATH_NOT_FOUND)";
                    case 87: return "参数错误 (ERROR_INVALID_PARAMETER) - 可能是无效的修饰键或键码";
                    case 1444: return "指定的窗口句柄无效 (ERROR_INVALID_WINDOW_HANDLE)";
                    default: return $"未知错误代码: {errorCode}";
                }
            }
            catch
            {
                return $"错误代码: {errorCode}";
            }
        }
    }

    /// <summary>
    /// 热键事件参数
    /// </summary>
    public class HotkeyEventArgs : EventArgs
    {
        /// <summary>
        /// 热键ID
        /// </summary>
        public string HotkeyId { get; set; } = string.Empty;

        /// <summary>
        /// 命令名称
        /// </summary>
        public string CommandName { get; set; } = string.Empty;

        /// <summary>
        /// 热键模型
        /// </summary>
        public HotkeyModel? Hotkey { get; set; }
    }
}