using Sai2Capture.Models;
using Sai2Capture.Styles;
using System.Windows;
using System.Windows.Controls;

namespace Sai2Capture.Views
{
    /// <summary>
    /// VideoExportSettingsDialog.xaml 的交互逻辑
    /// </summary>
    public partial class VideoExportSettingsDialog : CustomDialogWindow
    {
        public VideoExportSettings Settings { get; private set; } = new();

        public VideoExportSettingsDialog()
        {
            InitializeComponent();
            InitializeControls();
        }

        public VideoExportSettingsDialog(VideoExportSettings? initialSettings = null) : this()
        {
            if (initialSettings != null)
            {
                ApplySettings(initialSettings);
            }
        }

        private void InitializeControls()
        {
            // 初始化编解码器列表
            CodecComboBox.ItemsSource = Enum.GetValues(typeof(VideoCodec))
                .Cast<VideoCodec>()
                .Select(c => c.GetDescription())
                .ToList();
            CodecComboBox.SelectedIndex = 0; // 默认 H.264

            // 质量滑块值变化事件
            QualitySlider.ValueChanged += (s, e) =>
            {
                QualityValueText.Text = $"{(int)QualitySlider.Value}%";
            };
        }

        private void ApplySettings(VideoExportSettings settings)
        {
            // 设置编解码器
            for (int i = 0; i < CodecComboBox.Items.Count; i++)
            {
                if (CodecComboBox.Items[i]!.ToString() == settings.Codec.GetDescription())
                {
                    CodecComboBox.SelectedIndex = i;
                    break;
                }
            }

            FpsNumberBox.Value = settings.Fps;
            WidthNumberBox.Value = settings.OutputWidth;
            HeightNumberBox.Value = settings.OutputHeight;
            QualitySlider.Value = settings.Quality;
        }

        public VideoExportSettings GetSettings()
        {
            var selectedCodecDescription = CodecComboBox.SelectedItem?.ToString() ?? "H.264 / AVC（推荐，最通用）";
            VideoCodec selectedCodec = VideoCodec.H264;

            foreach (VideoCodec codec in Enum.GetValues(typeof(VideoCodec)))
            {
                if (codec.GetDescription() == selectedCodecDescription)
                {
                    selectedCodec = codec;
                    break;
                }
            }

            return new VideoExportSettings
            {
                Codec = selectedCodec,
                Fps = FpsNumberBox.Value ?? 20,
                OutputWidth = (int)(WidthNumberBox.Value ?? 0),
                OutputHeight = (int)(HeightNumberBox.Value ?? 0),
                Quality = (int)QualitySlider.Value
            };
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            Settings = GetSettings();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
