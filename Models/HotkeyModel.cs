using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Sai2Capture.Models
{
    /// <summary>
    /// 热键配置模型
    /// 表示一个可配置的快捷键功能
    /// </summary>
    public partial class HotkeyModel : ObservableObject
    {
        /// <summary>
        /// 热键唯一标识符
        /// </summary>
        [ObservableProperty]
        private string _id = string.Empty;

        /// <summary>
        /// 热键显示名称
        /// 用于界面显示和用户识别
        /// </summary>
        [ObservableProperty]
        private string _name = string.Empty;

        /// <summary>
        /// 热键功能描述
        /// 说明该快捷键的作用
        /// </summary>
        [ObservableProperty]
        private string _description = string.Empty;

        /// <summary>
        /// 当前配置的快捷键
        /// 格式：Ctrl+Shift+F5
        /// </summary>
        [ObservableProperty]
        private string _currentKey = string.Empty;

        /// <summary>
        /// 默认快捷键
        /// 作为重置时的基准值
        /// </summary>
        [ObservableProperty]
        private string _defaultKey = string.Empty;

        /// <summary>
        /// 是否启用该热键
        /// 允许临时禁用特定快捷键
        /// </summary>
        [ObservableProperty]
        private bool _isEnabled = true;

        /// <summary>
        /// 是否需要修饰键（Ctrl, Alt, Shift）
        /// 一些安全操作需要修饰键避免误触发
        /// </summary>
        [ObservableProperty]
        private bool _requiresModifier = true;

        /// <summary>
        /// 功能命令名称
        /// 对应ViewModel中的RelayCommand
        /// </summary>
        [ObservableProperty]
        private string _commandName = string.Empty;

        /// <summary>
        /// 判断快捷键是否有变化
        /// </summary>
        public bool HasChanged => CurrentKey != DefaultKey;

        /// <summary>
        /// 创建默认的热键配置集合
        /// </summary>
        public static ObservableCollection<HotkeyModel> CreateDefaultHotkeys()
        {
            return new ObservableCollection<HotkeyModel>
            {
                new HotkeyModel
                {
                    Id = "start_capture",
                    Name = "开始录制",
                    Description = "开始屏幕录制",
                    DefaultKey = "F9",
                    CurrentKey = "F9",
                    CommandName = "StartCaptureCommand"
                },
                new HotkeyModel
                {
                    Id = "pause_capture",
                    Name = "暂停录制",
                    Description = "暂停当前录制",
                    DefaultKey = "F10",
                    CurrentKey = "F10",
                    CommandName = "PauseCaptureCommand"
                },
                new HotkeyModel
                {
                    Id = "stop_capture",
                    Name = "停止录制",
                    Description = "停止录制并保存视频",
                    DefaultKey = "F11",
                    CurrentKey = "F11",
                    CommandName = "StopCaptureCommand"
                },
                new HotkeyModel
                {
                    Id = "refresh_window_list",
                    Name = "刷新窗口列表",
                    Description = "刷新可选择的窗口列表",
                    DefaultKey = "F5",
                    CurrentKey = "F5",
                    CommandName = "RefreshWindowListCommand"
                },
                new HotkeyModel
                {
                    Id = "preview_window",
                    Name = "预览窗口",
                    Description = "打开/聚焦窗口预览",
                    DefaultKey = "F4",
                    CurrentKey = "F4",
                    CommandName = "PreviewWindowCommand"
                },
                new HotkeyModel
                {
                    Id = "toggle_window_topmost",
                    Name = "切换窗口置顶",
                    Description = "切换当前窗口是否置顶显示",
                    DefaultKey = "Ctrl+F4",
                    CurrentKey = "Ctrl+F4",
                    CommandName = "", // 需要单独处理
                    RequiresModifier = true
                },
                new HotkeyModel
                {
                    Id = "export_log",
                    Name = "导出日志",
                    Description = "导出当前日志内容",
                    DefaultKey = "Ctrl+E",
                    CurrentKey = "Ctrl+E",
                    CommandName = "ExportLogCommand",
                    RequiresModifier = true
                }
            };
        }

        /// <summary>
        /// 将快捷键字符串解析为键码和修饰键
        /// </summary>
        public static (int keyCode, bool ctrl, bool alt, bool shift) ParseKeyCombination(string keyString)
        {
            bool ctrl = false;
            bool alt = false;
            bool shift = false;
            int keyCode = 0;

            var parts = keyString.ToUpper().Split('+');
            
            foreach (var part in parts)
            {
                switch (part)
                {
                    case "CTRL":
                        ctrl = true;
                        break;
                    case "ALT":
                        alt = true;
                        break;
                    case "SHIFT":
                        shift = true;
                        break;
                    case "WIN":
                        // Windows键暂时不支持
                        break;
                    default:
                        // 处理功能键和字母键
                        if (part.StartsWith("F") && part.Length > 1)
                        {
                            if (int.TryParse(part.Substring(1), out int fKey))
                            {
                                // F1-F12对应112-123
                                keyCode = 111 + fKey;
                            }
                        }
                        else if (part.Length == 1 && char.IsLetter(part[0]))
                        {
                            keyCode = (int)part[0];
                        }
                        break;
                }
            }

            return (keyCode, ctrl, alt, shift);
        }

        /// <summary>
        /// 验证快捷键是否合法
        /// </summary>
        public static bool ValidateKeyCombination(string keyString)
        {
            if (string.IsNullOrWhiteSpace(keyString))
                return false;

            var parts = keyString.ToUpper().Split('+');
            int keyCount = 0;

            foreach (var part in parts)
            {
                switch (part)
                {
                    case "CTRL":
                    case "ALT":
                    case "SHIFT":
                    case "WIN":
                        continue;
                    default:
                        // 检查功能键 F1-F12
                        if (part.StartsWith("F") && part.Length > 1)
                        {
                            if (int.TryParse(part.Substring(1), out int fKey))
                            {
                                if (fKey >= 1 && fKey <= 12)
                                {
                                    keyCount++;
                                    continue;
                                }
                            }
                        }
                        // 检查单个字母
                        else if (part.Length == 1 && char.IsLetter(part[0]))
                        {
                            keyCount++;
                            continue;
                        }
                        // 检查其他常用键
                        else if (IsValidSpecialKey(part))
                        {
                            keyCount++;
                            continue;
                        }
                        return false;
                }
            }

            // 至少要有一个非修饰键
            return keyCount > 0;
        }

        /// <summary>
        /// 检查是否为有效的特殊键
        /// </summary>
        private static bool IsValidSpecialKey(string key)
        {
            string[] validKeys = {
                "ESC", "ENTER", "SPACE", "TAB", "BACKSPACE", "DELETE", "INSERT",
                "HOME", "END", "PAGEUP", "PAGEDOWN",
                "UP", "DOWN", "LEFT", "RIGHT",
                "PRINTSCREEN", "SCROLLLOCK", "PAUSE"
            };

            return System.Array.Exists(validKeys, k => k == key.ToUpper());
        }

        /// <summary>
        /// 重置为默认快捷键
        /// </summary>
        public void ResetToDefault()
        {
            CurrentKey = DefaultKey;
        }
    }
}