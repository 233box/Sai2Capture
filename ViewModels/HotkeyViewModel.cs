using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Models;
using Sai2Capture.Services;
using Sai2Capture.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 热键配置视图模型
    /// 管理热键配置的显示、编辑和保存
    /// </summary>
    public partial class HotkeyViewModel : ObservableObject
    {
        private readonly HotkeyService _hotkeyService;
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;

        /// <summary>
        /// 热键配置列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<HotkeyModel> _hotkeys = new();

        /// <summary>
        /// 选中的热键项
        /// </summary>
        [ObservableProperty]
        private HotkeyModel? _selectedHotkey;


        /// <summary>
        /// 正在编辑的热键项
        /// </summary>
        [ObservableProperty]
        private HotkeyModel? _editingHotkey;

        /// <summary>
        /// 编辑对话框标题
        /// </summary>
        [ObservableProperty]
        private string _editDialogTitle = "编辑热键";

        /// <summary>
        /// 热键是否启用状态
        /// </summary>
        [ObservableProperty]
        private bool _hotkeysEnabled = true;

        /// <summary>
        /// 初始化热键视图模型
        /// </summary>
        public HotkeyViewModel(
            HotkeyService hotkeyService,
            SettingsService settingsService,
            LogService logService)
        {
            _hotkeyService = hotkeyService;
            _settingsService = settingsService;
            _logService = logService;

            // 订阅热键服务的事件
            hotkeyService.OnHotkeyTriggered += OnHotkeyTriggered;

            // 初始加载热键配置（仅引用，不触发注册）
            LoadHotkeys();

            // 同步热键启用状态
            HotkeysEnabled = _hotkeyService.HotkeysEnabled;
        }

        /// <summary>
        /// 加载热键配置
        /// </summary>
        public void LoadHotkeys()
        {
            try
            {
                // 直接使用热键服务的集合
                Hotkeys = _hotkeyService.Hotkeys;
                
                if (Hotkeys == null || !Hotkeys.Any())
                {
                    _logService.AddLog("热键集合为空，将在窗口初始化时加载", LogLevel.Warning);
                }
                else
                {
                    _logService.AddLog($"热键配置已加载: {Hotkeys.Count} 个热键");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载热键配置失败: {ex.Message}", LogLevel.Error);
                // 失败时使用默认配置
                Hotkeys = new ObservableCollection<HotkeyModel>(HotkeyModel.CreateDefaultHotkeys());
                _logService.AddLog($"创建了 {Hotkeys.Count} 个默认热键配置作为备用");
            }
        }

        /// <summary>
        /// 打开热键编辑对话框
        /// </summary>
        [RelayCommand]
        private void EditHotkey(HotkeyModel hotkey)
        {
            if (hotkey == null) return;

            EditingHotkey = new HotkeyModel
            {
                Id = hotkey.Id,
                Name = hotkey.Name,
                Description = hotkey.Description,
                CurrentKey = hotkey.CurrentKey,
                DefaultKey = hotkey.DefaultKey,
                IsEnabled = hotkey.IsEnabled,
                RequiresModifier = hotkey.RequiresModifier,
                CommandName = hotkey.CommandName
            };

            EditDialogTitle = $"编辑热键: {hotkey.Name}";

            // 创建并显示编辑对话框
            var editDialog = new Views.HotkeyEditDialog(this);
            editDialog.Owner = System.Windows.Application.Current.MainWindow;
            editDialog.ShowDialog();
        }

        /// <summary>
        /// 保存编辑的热键配置
        /// </summary>
        [RelayCommand]
        private void SaveEditedHotkey()
        {
            try
            {
                if (EditingHotkey == null) return;

                // 验证热键格式
                if (!HotkeyModel.ValidateKeyCombination(EditingHotkey.CurrentKey))
                {
                    CustomDialogService.ShowDialog(
                        "无效的热键格式！\n\n请确保：\n1. 至少包含一个非修饰键（F1-F12、字母键等）\n2. 使用有效的组合键格式，如 Ctrl+Shift+F5",
                        "错误",
                        "确定");
                    return;
                }

                // 检查快捷键是否重复
                var duplicate = Hotkeys.FirstOrDefault(h => 
                    h.Id != EditingHotkey.Id && 
                    h.CurrentKey.ToUpper() == EditingHotkey.CurrentKey.ToUpper());
                
                if (duplicate != null)
                {
                    CustomDialogService.ShowDialog(
                        $"快捷键 '{EditingHotkey.CurrentKey}' 已被 '{duplicate.Name}' 占用！\n\n请选择其他快捷键组合。",
                        "快捷键冲突",
                        "确定");
                    return;
                }

                // 更新原热键配置
                var targetHotkey = Hotkeys.FirstOrDefault(h => h.Id == EditingHotkey.Id);
                if (targetHotkey != null)
                {
                    targetHotkey.CurrentKey = EditingHotkey.CurrentKey;
                    targetHotkey.IsEnabled = EditingHotkey.IsEnabled;

                    // 保存到热键服务
                    _hotkeyService.UpdateHotkey(targetHotkey.Id, targetHotkey.CurrentKey);
                    
                    _logService.AddLog($"热键更新: {targetHotkey.Name} = {targetHotkey.CurrentKey}");
                }

// ShowEditDialog 不再需要，因为使用独立窗口
                SaveHotkeys();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键编辑失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 取消编辑
        /// </summary>
        [RelayCommand]
        private void CancelEdit()
        {
            // ShowEditDialog 不再需要，因为使用独立窗口
            EditingHotkey = null;
        }

        /// <summary>
        /// 重置单个热键为默认值
        /// </summary>
        [RelayCommand]
        private void ResetHotkey(HotkeyModel hotkey)
        {
            if (hotkey == null || !hotkey.HasChanged) return;

            var result = CustomDialogService.ShowDialog(
                $"确定要将 '{hotkey.Name}' 重置为默认快捷键 '{hotkey.DefaultKey}' 吗？",
                "确认重置",
                "重置",
                "取消");

            if (result)
            {
                hotkey.ResetToDefault();
                _hotkeyService.UpdateHotkey(hotkey.Id, hotkey.CurrentKey);
                SaveHotkeys();
                _logService.AddLog($"热键重置: {hotkey.Name} = {hotkey.DefaultKey}");
            }
        }

        /// <summary>
        /// 重置所有热键为默认值
        /// </summary>
        [RelayCommand]
        private void ResetAllHotkeys()
        {
            var result = CustomDialogService.ShowDialog(
                "确定要将所有热键重置为默认设置吗？\n\n此操作将恢复所有快捷键到初始状态，自定义设置将被清除。",
                "确认重置全部",
                "全部重置",
                "取消");

            if (result)
            {
                _hotkeyService.ResetToDefaults();
                LoadHotkeys();
                SaveHotkeys();
                _logService.AddLog("所有热键已重置为默认设置");
            }
        }

        /// <summary>
        /// 保存热键配置
        /// </summary>
        [RelayCommand]
        private void SaveHotkeys()
        {
            try
            {
                _hotkeyService.SaveHotkeys();
                _logService.AddLog("热键配置已保存");
                CustomDialogService.ShowDialog("热键配置已保存成功！", "保存成功", "确定");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键配置失败: {ex.Message}", LogLevel.Error);
                CustomDialogService.ShowDialog($"保存热键配置失败: {ex.Message}", "保存失败", "确定");
            }
        }

        /// <summary>
        /// 切换热键启用状态
        /// </summary>
        [RelayCommand]
        private void ToggleHotkeysEnabled()
        {
            HotkeysEnabled = !HotkeysEnabled;
            _hotkeyService.HotkeysEnabled = HotkeysEnabled;
            
            var status = HotkeysEnabled ? "已启用" : "已禁用";
            _logService.AddLog($"热键功能{status}");
        }

        /// <summary>
        /// 打开热键捕捉对话框
        /// </summary>
        [RelayCommand]
        private void OpenHotkeyCaptureDialog()
        {
            var dialog = new HotkeyCaptureDialog();
            var result = dialog.ShowDialog();

            if (result.HasValue && result.Value && dialog.CapturedKey != null)
            {
                // 在编辑模式下直接更新当前编辑的热键
                if (EditingHotkey != null)
                {
                    EditingHotkey.CurrentKey = dialog.CapturedKey;
                }
                else if (SelectedHotkey != null)
                {
                    SelectedHotkey.CurrentKey = dialog.CapturedKey;
                    _hotkeyService.UpdateHotkey(SelectedHotkey.Id, SelectedHotkey.CurrentKey);
                    SaveHotkeys();
                }
            }
        }

        /// <summary>
        /// 热键触发事件处理
        /// </summary>
        private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
        {
            if (e.Hotkey != null)
            {
                _logService.AddLog($"热键触发: {e.Hotkey.Name} ({e.CommandName})");
            }

            // 这里的热键命令执行逻辑由MainViewModel处理
            // 这个视图模型主要负责配置管理
        }

        /// <summary>
        /// 获取热键的显示提示
        /// </summary>
        public string GetHotkeyTooltip(HotkeyModel hotkey)
        {
            if (!hotkey.IsEnabled)
                return $"{hotkey.Name} (已禁用)";

            var changed = hotkey.HasChanged ? " (已自定义)" : "";
            return $"{hotkey.Name}: {hotkey.CurrentKey}{changed}\n\n{hotkey.Description}";
        }

        /// <summary>
        /// 导出热键配置
        /// </summary>
        [RelayCommand]
        private void ExportHotkeys()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    FileName = $"Sai2Capture_Hotkeys_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var exportData = Hotkeys.Select(h => new
                    {
                        h.Id,
                        h.Name,
                        h.Description,
                        h.CurrentKey,
                        h.IsEnabled
                    }).ToList();

                    string json = System.Text.Json.JsonSerializer.Serialize(exportData, 
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    
                    System.IO.File.WriteAllText(dialog.FileName, json);
                    _logService.AddLog($"热键配置已导出到: {dialog.FileName}");
                    CustomDialogService.ShowDialog($"热键配置已成功导出到:\n{dialog.FileName}", "导出成功", "确定");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"导出热键配置失败: {ex.Message}", LogLevel.Error);
                CustomDialogService.ShowDialog($"导出热键配置失败: {ex.Message}", "导出失败", "确定");
            }
        }

        /// <summary>
        /// 导入热键配置
        /// </summary>
        [RelayCommand]
        private void ImportHotkeys()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "选择要导入的热键配置文件"
                };

                if (dialog.ShowDialog() == true)
                {
                    string json = System.IO.File.ReadAllText(dialog.FileName);
                    var importData = System.Text.Json.JsonSerializer.Deserialize<
                        System.Collections.Generic.List<System.Text.Json.JsonElement>>(json);

                    if (importData == null || !importData.Any())
                    {
                        CustomDialogService.ShowDialog("导入文件格式无效或无数据！", "导入失败", "确定");
                        return;
                    }

                    var confirm = CustomDialogService.ShowDialog(
                        $"确定要从 '{System.IO.Path.GetFileName(dialog.FileName)}' 导入热键配置吗？\n\n注意：此操作将覆盖当前所有热键配置！",
                        "确认导入",
                        "导入",
                        "取消");

                    if (!confirm) return;

                    int importedCount = 0;
                    foreach (var importedItem in importData)
                    {
                        if (importedItem.TryGetProperty("Id", out var idElement) &&
                            importedItem.TryGetProperty("CurrentKey", out var keyElement))
                        {
                            var id = idElement.GetString();
                            var currentKey = keyElement.GetString();

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(currentKey))
                            {
                                var hotkey = Hotkeys.FirstOrDefault(h => h.Id == id);
                                if (hotkey != null)
                                {
                                    hotkey.CurrentKey = currentKey;
                                    importedCount++;
                                }
                            }
                        }
                    }

                    // 保存并重新应用热键
                    SaveHotkeys();
                    _hotkeyService.RegisterAllHotkeys();

                    CustomDialogService.ShowDialog(
                        $"成功导入 {importedCount} 个热键配置！",
                        "导入成功",
                        "确定");

                    _logService.AddLog($"从 {dialog.FileName} 导入了 {importedCount} 个热键配置");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"导入热键配置失败: {ex.Message}", LogLevel.Error);
                CustomDialogService.ShowDialog($"导入热键配置失败: {ex.Message}", "导入失败", "确定");
            }
        }
    }
}