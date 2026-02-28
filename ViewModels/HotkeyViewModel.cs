using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sai2Capture.Models;
using Sai2Capture.Services;
using Sai2Capture.Views;
using System.Collections.ObjectModel;

namespace Sai2Capture.ViewModels
{
    /// <summary>
    /// 热键配置视图模型
    /// </summary>
    public partial class HotkeyViewModel : ObservableObject
    {
        private readonly HotkeyService _hotkeyService;
        private readonly LogService _logService;
        private readonly SettingsService _settingsService;
        private System.Windows.Window? _currentEditDialog;

        [ObservableProperty] private ObservableCollection<HotkeyModel> _hotkeys = new();
        [ObservableProperty] private HotkeyModel? _selectedHotkey;
        [ObservableProperty] private HotkeyModel? _editingHotkey;
        [ObservableProperty] private string _editDialogTitle = "编辑热键";
        [ObservableProperty] private bool _hotkeysEnabled = true;

        public HotkeyViewModel(HotkeyService hotkeyService, SettingsService settingsService, LogService logService)
        {
            _hotkeyService = hotkeyService;
            _settingsService = settingsService;
            _logService = logService;
            hotkeyService.OnHotkeyTriggered += OnHotkeyTriggered;
            LoadHotkeys();
            HotkeysEnabled = _hotkeyService.HotkeysEnabled;
        }

        public void LoadHotkeys()
        {
            try
            {
                Hotkeys = _hotkeyService.Hotkeys;
                if (!Hotkeys.Any())
                {
                    _logService.AddLog("热键集合为空，将在窗口初始化时加载", LogLevel.Warning);
                }
                else
                {
                    _logService.AddLog($"热键配置已加载：{Hotkeys.Count} 个热键");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"加载热键配置失败：{ex.Message}", LogLevel.Error);
                Hotkeys = new ObservableCollection<HotkeyModel>(HotkeyModel.CreateDefaultHotkeys());
                _logService.AddLog($"创建了 {Hotkeys.Count} 个默认热键配置作为备用");
            }
        }

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

            EditDialogTitle = $"编辑热键：{hotkey.Name}";
            var editDialog = new HotkeyEditDialog(this) { Owner = System.Windows.Application.Current.MainWindow };
            _currentEditDialog = editDialog;
            editDialog.ShowDialog();
            _currentEditDialog = null;
        }

        [RelayCommand]
        private void SaveEditedHotkey()
        {
            try
            {
                if (EditingHotkey == null) return;

                if (!HotkeyModel.ValidateKeyCombination(EditingHotkey.CurrentKey))
                {
                    ShowErrorDialog("无效的热键格式！\n\n请确保：\n1. 至少包含一个非修饰键（F1-F12、字母键等）\n2. 使用有效的组合键格式，如 Ctrl+Shift+F5");
                    return;
                }

                var duplicate = Hotkeys.FirstOrDefault(h => h.Id != EditingHotkey.Id && h.CurrentKey.ToUpper() == EditingHotkey.CurrentKey.ToUpper());
                if (duplicate != null)
                {
                    ShowErrorDialog($"快捷键 '{EditingHotkey.CurrentKey}' 已被 '{duplicate.Name}' 占用！\n\n请选择其他快捷键组合。");
                    return;
                }

                var targetHotkey = Hotkeys.FirstOrDefault(h => h.Id == EditingHotkey.Id);
                if (targetHotkey != null)
                {
                    targetHotkey.CurrentKey = EditingHotkey.CurrentKey;
                    targetHotkey.IsEnabled = EditingHotkey.IsEnabled;
                    _hotkeyService.UpdateHotkey(targetHotkey.Id, targetHotkey.CurrentKey);
                    _logService.AddLog($"热键更新：{targetHotkey.Name} = {targetHotkey.CurrentKey}");
                }

                _hotkeyService.SaveHotkeys();
                _logService.AddLog("热键配置已保存");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键编辑失败：{ex.Message}", LogLevel.Error);
            }
        }

        private void ShowErrorDialog(string message)
        {
            var errorDialog = new HotkeyErrorDialog
            {
                Title = "错误",
                Message = message,
                Owner = _currentEditDialog ?? System.Windows.Application.Current.MainWindow
            };
            errorDialog.ShowDialog();
        }

        [RelayCommand]
        private void CancelEdit() => EditingHotkey = null;

        [RelayCommand]
        private void ResetHotkey(HotkeyModel hotkey)
        {
            if (hotkey == null || !hotkey.HasChanged) return;

            if (CustomDialogService.ShowDialog($"确定要将 '{hotkey.Name}' 重置为默认快捷键 '{hotkey.DefaultKey}' 吗？", "确认重置", "重置", "取消"))
            {
                hotkey.ResetToDefault();
                _hotkeyService.UpdateHotkey(hotkey.Id, hotkey.CurrentKey);
                _hotkeyService.SaveHotkeys();
                _logService.AddLog($"热键重置：{hotkey.Name} = {hotkey.DefaultKey}");
            }
        }

        [RelayCommand]
        private void ResetAllHotkeys()
        {
            if (CustomDialogService.ShowDialog("确定要将所有热键重置为默认设置吗？\n\n此操作将恢复所有快捷键到初始状态，自定义设置将被清除。", "确认重置全部", "全部重置", "取消"))
            {
                _hotkeyService.ResetToDefaults();
                LoadHotkeys();
                _hotkeyService.SaveHotkeys();
                _logService.AddLog("所有热键已重置为默认设置");
            }
        }

        [RelayCommand]
        private void ToggleHotkeysEnabled()
        {
            HotkeysEnabled = !HotkeysEnabled;
            _hotkeyService.HotkeysEnabled = HotkeysEnabled;
            _logService.AddLog($"热键功能{(HotkeysEnabled ? "已启用" : "已禁用")}");
        }

        [RelayCommand]
        private void OpenHotkeyCaptureDialog()
        {
            var dialog = new HotkeyCaptureDialog();
            if (dialog.ShowDialog() == true && dialog.CapturedKey != null)
            {
                if (EditingHotkey != null)
                {
                    EditingHotkey.CurrentKey = dialog.CapturedKey;
                }
                else if (SelectedHotkey != null)
                {
                    SelectedHotkey.CurrentKey = dialog.CapturedKey;
                    _hotkeyService.UpdateHotkey(SelectedHotkey.Id, SelectedHotkey.CurrentKey);
                    _hotkeyService.SaveHotkeys();
                    _logService.AddLog($"热键更新：{SelectedHotkey.Name} = {SelectedHotkey.CurrentKey}");
                }
            }
        }

        private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
        {
            if (e.Hotkey != null)
                _logService.AddLog($"热键触发：{e.Hotkey.Name} ({e.CommandName})");
        }

        public string GetHotkeyTooltip(HotkeyModel hotkey)
        {
            if (!hotkey.IsEnabled) return $"{hotkey.Name} (已禁用)";
            var changed = hotkey.HasChanged ? " (已自定义)" : "";
            return $"{hotkey.Name}: {hotkey.CurrentKey}{changed}\n\n{hotkey.Description}";
        }

        [RelayCommand]
        private void ExportHotkeys()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    FileName = $"Sai2Capture_Hotkeys_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var exportData = Hotkeys.Select(h => new { h.Id, h.Name, h.Description, h.CurrentKey, h.IsEnabled }).ToList();
                    string json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dialog.FileName, json);
                    _logService.AddLog($"热键配置已导出到：{dialog.FileName}");
                    CustomDialogService.ShowDialog($"热键配置已成功导出到:\n{dialog.FileName}", "导出成功", "确定");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"导出热键配置失败：{ex.Message}", LogLevel.Error);
                CustomDialogService.ShowDialog($"导出热键配置失败：{ex.Message}", "导出失败", "确定");
            }
        }

        [RelayCommand]
        private void ImportHotkeys()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "选择要导入的热键配置文件"
                };

                if (dialog.ShowDialog() == true)
                {
                    string json = System.IO.File.ReadAllText(dialog.FileName);
                    var importData = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Text.Json.JsonElement>>(json);

                    if (importData == null || !importData.Any())
                    {
                        CustomDialogService.ShowDialog("导入文件格式无效或无数据！", "导入失败", "确定");
                        return;
                    }

                    if (!CustomDialogService.ShowDialog($"确定要从 '{System.IO.Path.GetFileName(dialog.FileName)}' 导入热键配置吗？\n\n注意：此操作将覆盖当前所有热键配置！", "确认导入", "导入", "取消"))
                        return;

                    int importedCount = 0;
                    foreach (var importedItem in importData)
                    {
                        if (importedItem.TryGetProperty("Id", out var idElement) && importedItem.TryGetProperty("CurrentKey", out var keyElement))
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

                    SaveHotkeys();
                    _hotkeyService.RegisterAllHotkeys();
                    CustomDialogService.ShowDialog($"成功导入 {importedCount} 个热键配置！", "导入成功", "确定");
                    _logService.AddLog($"从 {dialog.FileName} 导入了 {importedCount} 个热键配置");
                }
            }
            catch (Exception ex)
            {
                _logService.AddLog($"导入热键配置失败：{ex.Message}", LogLevel.Error);
                CustomDialogService.ShowDialog($"导入热键配置失败：{ex.Message}", "导入失败", "确定");
            }
        }

        private void SaveHotkeys()
        {
            try
            {
                _hotkeyService.SaveHotkeys();
                _logService.AddLog("热键配置已保存");
            }
            catch (Exception ex)
            {
                _logService.AddLog($"保存热键配置失败：{ex.Message}", LogLevel.Error);
            }
        }
    }
}
