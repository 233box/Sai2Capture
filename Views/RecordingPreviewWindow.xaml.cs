using OpenCvSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace Sai2Capture.Views
{
    public partial class RecordingPreviewWindow : System.Windows.Window
    {
        public RecordingPreviewWindow(Mat frame)
        {
            InitializeComponent();
            
            // 将 OpenCV Mat 转换为 BitmapSource 并显示
            try
            {
                using var memoryStream = new MemoryStream();
                Cv2.ImEncode(".png", frame, out var imageData);
                memoryStream.Write(imageData, 0, imageData.Length);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();

                PreviewImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载预览失败：{ex.Message}", "错误", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }
    }
}
