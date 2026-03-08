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

            // 初始化质量等级列表
            QualityComboBox.ItemsSource = VideoExportSettings.QualityLevels
                .Select(q => q.Description)
                .ToList();
            QualityComboBox.SelectedIndex = 1; // 默认高质量（推荐）
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

            // 设置质量等级
            for (int i = 0; i < QualityComboBox.Items.Count; i++)
            {
                if (QualityComboBox.Items[i]!.ToString() == settings.QualityDescription)
                {
                    QualityComboBox.SelectedIndex = i;
                    break;
                }
            }
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

            // 获取质量等级
            int qualityLevel = 2;
            var selectedQualityDescription = QualityComboBox.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedQualityDescription))
            {
                var qualityOption = VideoExportSettings.QualityLevels
                    .FirstOrDefault(q => q.Description == selectedQualityDescription);
                qualityLevel = qualityOption.Level;
            }

            return new VideoExportSettings
            {
                Codec = selectedCodec,
                Fps = FpsNumberBox.Value ?? 20,
                QualityLevel = qualityLevel
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
