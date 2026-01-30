using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.Collections.Generic;

namespace Sai2Capture.Services
{
    public partial class SharedStateService : ObservableObject
    {
        [ObservableProperty]
        private int _cutWindow = 70;

        [ObservableProperty]
        private bool _running = false;

        [ObservableProperty]
        private int _frameNumber = 0;

        [ObservableProperty]
        private Mat? _lastImage = null;

        [ObservableProperty]
        private bool _isTopmost = false;

        [ObservableProperty]
        private string _outputFolder = "";

        [ObservableProperty]
        private nint _hwnd = nint.Zero;

        [ObservableProperty]
        private double _interval = 0.5;

        [ObservableProperty]
        private bool _firstStart = false;

        [ObservableProperty]
        private int _savedCount = 0;

        // 视频写入器
        public VideoWriter? VideoWriter { get; set; }

        // 视频路径
        public string? VideoPath { get; set; }

        // 窗口标题列表缓存
        public List<string> WindowTitles { get; set; } = new List<string>();
    }
}
