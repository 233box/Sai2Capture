using System.Windows;
using Sai2Capture.Models;
using Sai2Capture.ViewModels;

namespace Sai2Capture.Views
{
    /// <summary>
    /// 热键编辑对话框
    /// </summary>
    public partial class HotkeyEditDialog : Sai2Capture.Styles.CustomDialogWindow
    {
        private readonly HotkeyViewModel _viewModel;
        
        /// <summary>
        /// 正在编辑的热键项
        /// </summary>
        public HotkeyModel? EditingHotkey => _viewModel.EditingHotkey;

        public HotkeyEditDialog(HotkeyViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            // 对话框样式已通过基类自动应用

            Loaded += HotkeyEditDialog_Loaded;
        }

        private void HotkeyEditDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置窗口标题
            Title = _viewModel.EditDialogTitle;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行保存逻辑
                _viewModel.SaveEditedHotkeyCommand.Execute(null);

                // 关闭窗口（不再显示额外的保存成功提示）
                DialogResult = true;
                Close();
            }
            catch
            {
                // 保存失败时不关闭窗口
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}