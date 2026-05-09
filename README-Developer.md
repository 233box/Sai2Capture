# Sai2Capture Developer Documentation

## 项目概述

Sai2Capture 是一个基于 C# WPF 的桌面应用程序，专门用于捕获 SAI2 绘画软件的窗口内容并生成视频文件。项目采用 MVVM 架构模式，使用现代 .NET 8 技术栈。

### 核心技术栈

- **框架**: .NET 8.0 (WPF)
- **架构**: MVVM (CommunityToolkit.Mvvm)
- **UI 库**: WPF-UI v3.1.0
- **图像处理**: OpenCvSharp4 4.8.0
- **窗口捕获**: Windows PrintWindow API
- **依赖注入**: Microsoft.Extensions.DependencyInjection 8.0.0
- **配置管理**: System.Text.Json 8.0.1

## 项目结构

```
Sai2Capture/
├── src/
│   ├── Converters/              # 值转换器
│   │   └── UniversalConverter.cs
│   ├── Models/                  # 数据模型
│   │   └── HotkeyModel.cs
│   ├── Services/                # 核心业务服务
│   │   ├── CaptureService.cs         # 录制生命周期控制
│   │   ├── CustomDialogService.cs    # 自定义对话框服务
│   │   ├── HotkeyService.cs          # 全局热键注册和管理
│   │   ├── LogService.cs             # 日志记录服务
│   │   ├── Sai2FileParser.cs         # SAI2 文件解析（画布尺寸）
│   │   ├── SettingsService.cs        # 用户配置持久化
│   │   ├── SharedStateService.cs     # 全局状态管理
│   │   ├── SoundService.cs           # 音效播放服务
│   │   ├── UtilityService.cs         # 实用工具
│   │   ├── VideoRepairService.cs     # 视频修复服务
│   │   └── WindowCaptureService.cs   # 窗口内容捕获
│   ├── Sounds/                  # 嵌入式音效资源 (*.wav)
│   ├── Styles/                  # UI 样式和窗口基类
│   │   ├── BaseWindow.cs
│   │   ├── Colors.xaml
│   │   ├── ControlStyles.xaml
│   │   ├── CustomWindowStyles.xaml
│   │   ├── WindowStyles.xaml
│   │   └── WindowTemplateConverter.cs
│   ├── ViewModels/              # 视图模型
│   │   ├── MainViewModel.cs
│   │   ├── HotkeyViewModel.cs
│   │   ├── RecordingManagerViewModel.cs
│   │   └── VideoRepairViewModel.cs
│   └── Views/                   # 用户界面
│       ├── HotkeyCaptureDialog.xaml(.cs)
│       ├── HotkeyEditDialog.xaml(.cs)
│       ├── HotkeyErrorDialog.xaml(.cs)
│       ├── LogPage.xaml(.cs)
│       ├── MainPage.xaml(.cs)
│       ├── RecordingManagerPage.xaml(.cs)
│       ├── RecordingPreviewWindow.xaml(.cs)
│       ├── SettingsPage.xaml(.cs)
│       └── VideoRepairPage.xaml(.cs)
├── App.xaml(.cs)                # 应用入口
├── MainWindow.xaml(.cs)         # 主窗口
└── Sai2Capture.csproj           # 项目文件
```

## 架构设计

### MVVM 架构

项目严格遵循 MVVM (Model-View-ViewModel) 模式：

- **Models**: 纯数据类，包含业务实体（如 `HotkeyModel`）
- **Views**: XAML 界面，专注于显示逻辑
- **ViewModels**: 连接 View 和 Model，处理 UI 逻辑和状态管理
- **Services**: 独立的业务逻辑服务，通过依赖注入注入到 ViewModel

### 核心服务架构

#### SharedStateService

集中管理应用程序全局状态，使用 `ObservableProperty` 支持数据绑定。管理状态包括：

- `Running` — 是否正在录制
- `IsInitialized` — 是否已初始化（暂停状态）
- `CanvasWidth/Height` — SAI2 画布尺寸
- `SavedCount` — 有效捕获帧数

#### CaptureService

控制录制生命周期，协调窗口捕获和视频生成流程：

- `StartCapture()` — 开始录制
- `PauseCapture()` — 暂停录制
- `StopCapture()` — 停止并生成视频
- 使用独立后台线程进行帧捕获

#### WindowCaptureService

使用 Windows PrintWindow API 实现窗口捕获：

- 使用传统 PrintWindow API 进行窗口内容捕获
- 支持 SAI2 窗口枚举和识别
- 帧差检测算法减少冗余帧保存
- 图像比较使用 OpenCV 的 `Absdiff` 和 `CountNonZero`

#### SettingsService

使用 JSON 格式持久化用户配置，支持实时保存和加载：

- 窗口位置和尺寸记忆
- 捕获间隔、窗口名称
- 保存路径、SAI2 程序路径
- 热键配置

#### LogService

日志记录服务，支持级别分类（Info/Warning/Error）：

- 循环缓冲区（最多 1000 条）
- 支持按级别过滤显示
- 事件通知 UI 更新

### 依赖注入配置

在 `App.xaml.cs` 中配置所有服务：

```csharp
private void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<SharedStateService>();
    services.AddSingleton<WindowCaptureService>();
    services.AddSingleton<CaptureService>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<LogService>();
    services.AddSingleton<HotkeyService>();
    services.AddSingleton<SoundService>();
    services.AddSingleton<MainViewModel>();
    // ...
}
```

## 核心功能实现

### 帧差检测算法

```csharp
// 在 WindowCaptureService 中实现
private bool ImagesEqual(Mat? img1, Mat img2)
{
    if (img1 == null) return false;
    if (img1.Size() != img2.Size()) return false;
    if (img1.Channels() != img2.Channels()) return false;

    using Mat diff = new Mat();
    Cv2.Absdiff(img1, img2, diff);

    if (diff.Channels() > 1)
    {
        using Mat gray = new Mat();
        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
        return Cv2.CountNonZero(gray) == 0;
    }
    else
    {
        return Cv2.CountNonZero(diff) == 0;
    }
}
```

### PrintWindow API 窗口捕获

```csharp
public Mat CaptureWindowContent(nint hWnd)
{
    if (!GetWindowRect(hWnd, out RECT windowRect))
        throw new Win32Exception(Marshal.GetLastWin32Error());

    int width = windowRect.Right - windowRect.Left;
    int height = windowRect.Bottom - windowRect.Top;

    using var bitmap = new System.Drawing.Bitmap(width, height);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        IntPtr hdc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(hWnd, hdc, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { graphics.ReleaseHdc(hdc); }
    }

    var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
    var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
    try
    {
        var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmapData.Scan0);
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);
        return mat.Clone();
    }
    finally { bitmap.UnlockBits(bitmapData); }
}
```

### 全局热键系统

使用 Win32 API 注册系统级热键：

```csharp
private void RegisterHotkey(HotkeyModel hotkey)
{
    var (keyCode, ctrl, alt, shift) = HotkeyModel.ParseKeyCombination(hotkey.CurrentKey);
    uint modifiers = 0;
    if (ctrl) modifiers |= 0x0002;
    if (alt) modifiers |= 0x0001;
    if (shift) modifiers |= 0x0004;

    int hotkeyId = _nextHotkeyId++;
    RegisterHotKey(_windowHandle, hotkeyId, modifiers, (uint)keyCode);
}
```

### 自定义窗口样式系统

```csharp
public class BaseWindow : Window
{
    private void InitializeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        if (WindowKind == WindowType.MainWindow)
            WindowTemplateHelper.ApplyCustomWindowStyle(this);
        else
            WindowTemplateHelper.ApplyCustomDialogStyle(this);
    }
}
```

### SAI2 画布尺寸监控

每 2 秒轮询一次 SAI2 进程窗口标题，解析 .sai2 文件路径获取画布尺寸。

## 开发环境搭建

### 前置要求

- Visual Studio 2022 (17.5+)
- .NET 8.0 SDK
- Windows 10/11 SDK (10.0.19041.0+)
- Git

### 克隆和构建

```bash
git clone https://github.com/your-username/Sai2Capture.git
cd Sai2Capture
dotnet restore
dotnet build --configuration Release
```

### 本地调试

1. 在 Visual Studio 中打开 `Sai2Capture.sln`
2. 设置启动项目为 `Sai2Capture`
3. 按 F5 开始调试

### 发布命令

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# 输出：bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/
```

## 内存管理策略

- 所有 `Mat` 对象使用 `using` 语句或手动 `Dispose()` 确保及时释放
- 实现了 `IDisposable` 接口的服务正确处理资源清理
- 限制日志条目数量，防止内存泄漏

## 常见问题

### 热键冲突

```csharp
private bool ValidateHotkeyCombination(HotkeyModel newHotkey)
{
    return !_registeredHotkeys.Values.Any(h =>
        h.Key == newHotkey.Key && h.Modifiers == newHotkey.Modifiers);
}
```

### 高 DPI 兼容

PrintWindow API 在高 DPI 环境下的处理：应用启动时调用 `SetProcessDPIAware()`。

## 提交规范

```
feat:     添加新功能
fix:      修复 bug
docs:     更新文档
style:    代码格式调整
refactor: 代码重构
test:     添加或修改测试
chore:    构建过程或辅助工具的变动
optimize: 性能优化
```

## 相关资源

- [WPF-UI Documentation](https://wpfui.lepo.co/)
- [OpenCvSharp Documentation](https://shimat.github.io/opencvsharp/)
- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/)
- [Windows PrintWindow API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)

---

**Happy coding!** 🚀
