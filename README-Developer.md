# 此文档由AI生成，部分内容可能存在不准确或错误，敬请谅解�?# Sai2Capture Developer Documentation

## 🏗�?项目概述

Sai2Capture 是一个基�?C# WPF 的桌面应用程序，专门用于捕获 SAI2 绘画软件的窗口内容并生成视频文件。项目采�?MVVM 架构模式，使用现�?.NET 8 技术�?
### 核心技术栈

- **框架**: .NET 8.0 (WPF)
- **架构**: MVVM (CommunityToolkit.Mvvm)
- **UI �?*: WPF-UI v3.1.0
- **视频处理**: OpenCvSharp4 4.8.0
- **窗口捕获**: Windows PrintWindow API
- **依赖注入**: Microsoft.Extensions.DependencyInjection 8.0.0
- **配置管理**: System.Text.Json 8.0.1

## 📁 项目结构

```
Sai2Capture/
├── Converters/          # 值转换器
├── Helpers/             # 辅助工具�?├── Models/              # 数据模型
�?  └── HotkeyModel.cs
├── Services/            # 核心业务服务
�?  ├── CaptureService.cs         # 录制生命周期控制
�?  ├── CustomDialogService.cs    # 自定义对话框服务
�?  ├── HotkeyService.cs          # 全局热键注册和管�?�?  ├── LogService.cs             # 日志记录服务
�?  ├── Sai2FileParser.cs         # SAI2 文件解析（画布尺寸）
�?  ├── SettingsService.cs        # 用户配置持久�?�?  ├── SharedStateService.cs     # 全局状态管�?�?  ├── SoundService.cs           # 音效播放服务
�?  ├── UtilityService.cs         # 实用工具（预览等�?�?  └── WindowCaptureService.cs   # 窗口内容捕获
├── Styles/              # UI 样式和窗口基�?�?  ├── BaseWindow.cs
�?  ├── Colors.xaml
�?  ├── ControlStyles.xaml
�?  ├── CustomWindowStyles.xaml
�?  ├── WindowStyles.xaml
�?  └── WindowTemplateConverter.cs
├── ViewModels/          # 视图模型
�?  ├── MainViewModel.cs
�?  ├── HotkeyViewModel.cs
�?  └── RecordingManagerViewModel.cs
├── Views/               # 用户界面
�?  ├── HotkeyCaptureDialog.xaml(.cs)
�?  ├── HotkeyEditDialog.xaml(.cs)
�?  ├── HotkeyErrorDialog.xaml(.cs)
�?  ├── LogPage.xaml(.cs)
�?  ├── MainPage.xaml(.cs)
�?  ├── RecordingManagerPage.xaml(.cs)
�?  ├── RecordingPreviewWindow.xaml(.cs)
�?  ├── SettingsPage.xaml(.cs)
�?  └── ConfirmDialog.xaml(.cs)
├── Sounds/              # 嵌入式音效资�?(*.wav)
├── logs/                # 日志导出目录
└── [Entry Points]       # 应用程序入口
    ├── App.xaml(.cs)
    ├── MainWindow.xaml(.cs)
    └── AssemblyInfo.cs
```

## 🏛�?架构设计

### MVVM 架构

项目严格遵循 MVVM (Model-View-ViewModel) 模式�?
- **Models**: 纯数据类，包含业务实体（�?`HotkeyModel`�?- **Views**: XAML 界面，专注于显示逻辑
- **ViewModels**: 连接 View �?Model，处�?UI 逻辑和状态管�?- **Services**: 独立的业务逻辑服务，通过依赖注入注入�?ViewModel

### 核心服务架构

#### SharedStateService
集中管理应用程序全局状态，使用 `ObservableProperty` 支持数据绑定。管理状态包括：
- `Running` - 是否正在录制
- `IsInitialized` - 是否已初始化（暂停状态）
- `CanvasWidth/Height` - SAI2 画布尺寸
- `SavedCount` - 有效捕获帧数

#### CaptureService
控制录制生命周期，协调窗口捕获和视频生成流程�?- `StartCapture()` - 开始录�?- `PauseCapture()` - 暂停录制
- `StopCapture()` - 停止并生成视�?- 使用独立后台线程进行帧捕�?
#### WindowCaptureService
使用 Windows PrintWindow API 实现窗口捕获�?- 使用传统 PrintWindow API 进行窗口内容捕获
- 支持 SAI2 窗口枚举和识�?- 帧差检测算法减少冗余帧保存
- 图像比较使用 OpenCV �?`Absdiff` �?`CountNonZero`

#### SettingsService
使用 JSON 格式持久化用户配置，支持实时保存和加载：
- 窗口位置和尺寸记�?- 捕获间隔、窗口名�?- 保存路径、SAI2 程序路径
- 热键配置

#### LogService
日志记录服务，支持级别分类（Info/Warning/Error）：
- 循环缓冲区（最�?1000 条）
- 支持按级别过滤显�?- 事件通知 UI 更新

### 依赖注入配置

�?`App.xaml.cs` 中配置所有服务：

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // 注册服务
    services.AddSingleton<SharedStateService>();
    services.AddSingleton<WindowCaptureService>();
    services.AddSingleton<UtilityService>();
    services.AddSingleton<CaptureService>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<LogService>();
    services.AddSingleton<HotkeyService>();
    services.AddSingleton<HotkeyViewModel>();

    // 注册 Dispatcher
    services.AddSingleton(provider =>
        Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);

    // 注册 ViewModel
    services.AddSingleton<MainViewModel>();
}
```

## 🔧 核心功能实现

### 1. 帧差检测算�?
```csharp
// �?WindowCaptureService 中实�?private bool ImagesEqual(Mat? img1, Mat img2)
{
    if (img1 == null) return false;
    if (img1.Size() != img2.Size()) return false;
    if (img1.Channels() != img2.Channels()) return false;

    using Mat diff = new Mat();
    Cv2.Absdiff(img1, img2, diff);

    // 对于多通道图像，需要先转换为灰度图再计数非零像�?    if (diff.Channels() > 1)
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

### 2. PrintWindow API 窗口捕获

```csharp
// �?WindowCaptureService �?public Mat CaptureWindowContent(nint hWnd)
{
    if (!GetWindowRect(hWnd, out RECT windowRect))
    {
        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    int width = windowRect.Right - windowRect.Left;
    int height = windowRect.Bottom - windowRect.Top;

    using var bitmap = new System.Drawing.Bitmap(width, height);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        IntPtr hdc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(hWnd, hdc, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    // 转换 Bitmap �?OpenCV Mat
    var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
    var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

    try
    {
        var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmapData.Scan0);
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);
        return mat.Clone();
    }
    finally
    {
        bitmap.UnlockBits(bitmapData);
    }
}
```

### 3. 全局热键系统

使用 Win32 API 注册系统级热键：

```csharp
// �?HotkeyService �?private void RegisterHotkey(HotkeyModel hotkey)
{
    var modifiers = GetModifierKeys(hotkey.Modifiers);
    var virtualKey = GetVirtualKey(hotkey.Key);

    bool success = RegisterHotKey(_windowHandle, hotkey.Id, modifiers, virtualKey);
    if (!success)
    {
        throw new HotkeyRegistrationException($"Failed to register hotkey: {hotkey}");
    }
}
```

### 4. 自定义窗口样式系�?
实现现代化的窗口外观和行为：

```csharp
// �?BaseWindow.cs �?public class BaseWindow : Window
{
    private void InitializeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;

        if (WindowKind == WindowType.MainWindow)
            WindowTemplateHelper.ApplyCustomWindowStyle(this);
        else
            WindowTemplateHelper.ApplyCustomDialogStyle(this);
    }
}
```

### 5. SAI2 画布尺寸监控

```csharp
// �?MainViewModel �?// �?2 秒轮询一�?SAI2 进程窗口标题，解�?.sai2 文件路径
private void UpdateCanvasSize()
{
    var sai2Processes = Process.GetProcessesByName("sai2");
    foreach (var proc in sai2Processes)
    {
        var title = proc.MainWindowTitle;
        // 解析 "标题 - 路径.sai2" 格式
        var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
        if (Sai2FileParser.TryParseCanvasSize(potentialPath, out int width, out int height))
        {
            CanvasSizeDisplay = $"SAI2 画布：{width} x {height}";
        }
    }
}
```

## 🛠�?开发环境搭�?
### 前置要求

- Visual Studio 2022 (17.5+)
- .NET 8.0 SDK
- Windows 10/11 SDK (10.0.19041.0+)
- Git

### 克隆和构�?
```bash
git clone https://github.com/your-username/Sai2Capture.git
cd Sai2Capture
dotnet restore
dotnet build --configuration Release
```

### 本地调试

1. �?Visual Studio 中打开 `Sai2Capture.sln`
2. 设置启动项目�?`Sai2Capture`
3. �?F5 开始调�?4. 调试输出会显示在 Visual Studio �?Output 窗口�?
### 单文件发布命�?
```bash
# 发布单文件版�?dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# 输出目录：bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/
```

### 依赖包管�?
```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.0" />
<PackageReference Include="OpenCvSharp4" Version="4.8.0.20230708" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.8.0.20230708" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
<PackageReference Include="System.Text.Json" Version="8.0.1" />
<PackageReference Include="WPF-UI" Version="3.1.0" />
```

## 🔍 关键设计决策

### 1. 为什么使�?PrintWindow API�?
- **兼容�?*: PrintWindow API 在所�?Windows 版本上稳定运�?- **简洁�?*: 单一捕获方式降低了代码复杂度
- **适用�?*: 对于 SAI2 的静态绘画场景，PrintWindow 性能足够

### 2. 为什么使�?OpenCvSharp4�?
- **稳定�?*: OpenCvSharp4 4.8.0 版本经过充分测试
- **功能完整�?*: 支持多种视频编码器和图像处理功能
- **生态成�?*: 丰富的文档和社区支持

### 3. 自定义窗口样式的实现

为了实现现代化的 UI 效果，项目使用自定义窗口样式�?
- 完全自定义窗口的边框、标题栏和控�?- 支持窗口透明效果
- 保持轻量级，无额�?UI 框架依赖

### 4. 内存管理策略

在大量图像处理场景中，特别注意内存管理：

- 所�?`Mat` 对象使用 `using` 语句或手�?`Clone()` 确保及时释放
- 实现�?`IDisposable` 接口的服务正确处理资源清�?- 限制日志条目数量，防止内存泄�?
## 🚧 扩展开发指�?
### 添加新的视频编码格式

```csharp
// �?CaptureService 中修改编码器配置
var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');
```

### 添加新的 UI 页面

1. �?`Views` 目录下创建新�?`.xaml` 文件
2. 创建对应�?`ViewModel`
3. �?`MainWindow.xaml` 中添加新�?`TabItem`
4. 在依赖注入容器中注册服务

### 性能建议

#### 图像处理

```csharp
// 使用 Clone() 确保 Mat 对象独立生命周期
_sharedState.LastImage = frame.Clone();
```

#### 异步操作

```csharp
// 确保 UI 响应�?public void StartCapture(string? windowTitle, bool useExactMatch, double interval)
{
    _captureThread = new Thread(CaptureLoop) { IsBackground = true };
    _captureThread.Start();
}
```

## 🧪 测试策略

### 关键测试场景

- 配置序列�?反序列化
- 帧差检测算法正确�?- 热键注册和取消注�?- 视频生成流程完整�?
### 性能测试

- 内存使用情况监控
- 大型窗体捕获性能
- 长时间录制稳定�?
## 🐛 常见问题

### 1. 热键冲突检�?
实现热键冲突检测逻辑�?
```csharp
private bool ValidateHotkeyCombination(HotkeyModel newHotkey)
{
    return !_registeredHotkeys.Values.Any(h =>
        h.Key == newHotkey.Key &&
        h.Modifiers == newHotkey.Modifiers);
}
```

### 2. �?DPI 显示器兼容�?
PrintWindow API 在高 DPI 环境下的处理�?
```csharp
// �?App.xaml.cs 中确保正确的 DPI 感知
// SetProcessDPIAware() 已在应用启动时调�?```

## 📝 贡献指南

### 代码规范

- 遵循 Microsoft C# 编码约定
- 使用 C# 12 特性（�?.NET 8 环境下）
- 为公共成员提�?XML 文档注释

### 提交规范

```
feat: 添加新功�?fix: 修复 bug
docs: 更新文档
style: 代码格式调整
refactor: 代码重构
test: 添加或修改测�?chore: 构建过程或辅助工具的变动
optimize: 性能优化
```

## 🔗 相关资源

- [WPF-UI Documentation](https://wpfui.lepo.co/)
- [OpenCvSharp Documentation](https://shimat.github.io/opencvsharp/)
- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/)
- [Windows PrintWindow API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)

---

**Happy coding!** 🚀
