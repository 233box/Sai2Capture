using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Sai2Capture.Views
{
    /// <summary>
    /// Interaction logic for HotkeyCaptureDialog.xaml
    /// </summary>
    public partial class HotkeyCaptureDialog : Window, INotifyPropertyChanged
    {
        private readonly HashSet<Key> _pressedKeys = new();
        private bool _canCaptureKeys = false;

        private string _capturedKeys = "";
        public string CapturedKeys
        {
            get => _capturedKeys;
            set
            {
                _capturedKeys = value;
                OnPropertyChanged();
            }
        }

        private string? _capturedKey = null;
        public string? CapturedKey
        {
            get => _capturedKey;
            set
            {
                _capturedKey = value;
                OnPropertyChanged();
            }
        }

        private string _status = "请按下您想要设置的快捷键...";
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public HotkeyCaptureDialog()
        {
            InitializeComponent();
            DataContext = this;

            // 应用自定义对话框样式
            Styles.WindowTemplateHelper.ApplyCustomDialogStyle(this);

            Loaded += HotkeyCaptureDialog_Loaded;
            Closing += HotkeyCaptureDialog_Closing;
        }

        private void HotkeyCaptureDialog_Loaded(object sender, RoutedEventArgs e)
        {
            _canCaptureKeys = true;
            Focus();
        }

        private void HotkeyCaptureDialog_Closing(object? sender, CancelEventArgs e)
        {
            _canCaptureKeys = false;
        }

        protected override void OnKeyDown(WpfKeyEventArgs e)
        {
            if (!_canCaptureKeys)
            {
                base.OnKeyDown(e);
                return;
            }

            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrEmpty(CapturedKeys))
                {
                    SaveAndClose();
                }
                return;
            }

            // 忽略一些特殊键
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                // 这些键将在OnKeyUp时处理
                return;
            }

            if (!_pressedKeys.Contains(e.Key))
            {
                _pressedKeys.Add(e.Key);
                UpdateCapturedKeys();
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(WpfKeyEventArgs e)
        {
            if (!_canCaptureKeys)
            {
                base.OnKeyUp(e);
                return;
            }

            e.Handled = true;

            // 处理修饰键释放
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                CaptureModifierKeys();
            }

            base.OnKeyUp(e);
        }

        private void CaptureModifierKeys()
        {
            var capturedKeysList = new List<string>();

            // 检查Ctrl键
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                capturedKeysList.Add("Ctrl");
            }

            // 检查Shift键
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                capturedKeysList.Add("Shift");
            }

            // 检查Alt键
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                capturedKeysList.Add("Alt");
            }

            // 将当前按下的非修饰键添加到列表中
            var nonModifierKeys = _pressedKeys.Where(k =>
                k != Key.LeftCtrl && k != Key.RightCtrl &&
                k != Key.LeftShift && k != Key.RightShift &&
                k != Key.LeftAlt && k != Key.RightAlt &&
                k != Key.LWin && k != Key.RWin &&
                k != Key.Escape && k != Key.Enter);

            foreach (var key in nonModifierKeys)
            {
                capturedKeysList.Add(KeyToReadableString(key));
            }

            // 更新显示
            if (capturedKeysList.Any())
            {
                CapturedKeys = string.Join("+", capturedKeysList);
                Status = "快捷键已捕获，按Enter确认，按Esc取消";
            }
            else
            {
                CapturedKeys = "";
                Status = "请按下您想要设置的快捷键...";
            }
        }

        private void UpdateCapturedKeys()
        {
            CaptureModifierKeys();
        }

        private static string KeyToReadableString(Key key)
        {
            return key switch
            {
                Key.F1 => "F1",
                Key.F2 => "F2",
                Key.F3 => "F3",
                Key.F4 => "F4",
                Key.F5 => "F5",
                Key.F6 => "F6",
                Key.F7 => "F7",
                Key.F8 => "F8",
                Key.F9 => "F9",
                Key.F10 => "F10",
                Key.F11 => "F11",
                Key.F12 => "F12",
                Key.F13 => "F13",
                Key.F14 => "F14",
                Key.F15 => "F15",
                Key.F16 => "F16",
                Key.F17 => "F17",
                Key.F18 => "F18",
                Key.F19 => "F19",
                Key.F20 => "F20",
                Key.F21 => "F21",
                Key.F22 => "F22",
                Key.F23 => "F23",
                Key.F24 => "F24",
                Key.Space => "Space",
                Key.Enter => "Enter",
                Key.Escape => "Escape",
                Key.Tab => "Tab",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Insert => "Insert",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Up => "Up",
                Key.Down => "Down",
                Key.Left => "Left",
                Key.Right => "Right",
                Key.PrintScreen => "PrintScreen",
                Key.Scroll => "ScrollLock",
                Key.Pause => "Pause",
                Key.D0 => "0",
                Key.D1 => "1",
                Key.D2 => "2",
                Key.D3 => "3",
                Key.D4 => "4",
                Key.D5 => "5",
                Key.D6 => "6",
                Key.D7 => "7",
                Key.D8 => "8",
                Key.D9 => "9",
                Key.NumPad0 => "Num0",
                Key.NumPad1 => "Num1",
                Key.NumPad2 => "Num2",
                Key.NumPad3 => "Num3",
                Key.NumPad4 => "Num4",
                Key.NumPad5 => "Num5",
                Key.NumPad6 => "Num6",
                Key.NumPad7 => "Num7",
                Key.NumPad8 => "Num8",
                Key.NumPad9 => "Num9",
                Key.Add => "NumAdd",
                Key.Subtract => "NumSubtract",
                Key.Multiply => "NumMultiply",
                Key.Divide => "NumDivide",
                Key.Decimal => "NumDecimal",
                Key.OemSemicolon => ";",
                Key.OemPlus => "+",
                Key.OemComma => ",",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemTilde => "`",
                Key.OemOpenBrackets => "[",
                Key.OemPipe => "\\",
                Key.OemCloseBrackets => "]",
                Key.OemQuotes => "'",
                _ when key >= Key.A && key <= Key.Z => key.ToString().ToUpper(),
                _ => key.ToString()
            };
        }

        private void SaveAndClose()
        {
            if (!string.IsNullOrEmpty(CapturedKeys))
            {
                CapturedKey = CapturedKeys;
                DialogResult = true;
                Close();
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _pressedKeys.Clear();
            CapturedKeys = "";
            CapturedKey = null;
            Status = "请按下您想要设置的快捷键...";
        }


        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}