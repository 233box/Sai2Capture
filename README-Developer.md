# 此文档由AI生成，部分内容可能存在不准确或错误，敬请谅解。
# Sai2Capture Developer Documentation

## 🏗️ 项目概述

Sai2Capture 是一个基于 C# WPF 的桌面应用程序，专门用于捕获 SAI2 绘画软件的窗口内容并生成视频文件。项目采用 MVVM 架构模式，使用现代 .NET 8 技术。

### 核心技术栈

- **框架**：.NET 8.0 (WPF + WindowsForms)
- **架构**：MVVM (CommunityToolkit.Mvvm)
- **UI库**：WPF-UI v3.1.0
- **视频处理**：OpenCvSharp4
- **窗口捕获**：Windows Graphics Capture (WGC) API
- **依赖注入**：Microsoft.Extensions.DependencyInjection
- **配置管理**：System.Text.Json

## 📁 项目结构

```
Sai2Capture/
├── Converters/          # 值转换器
│   └── UniversalConverter.cs
├── Models/              # 数据模型
│   ├── HotkeyModel.cs
│   ├── LogEntry.cs
│   └── SettingsModel.cs
├── Services/            # 核心业务服务
│   ├── CaptureService.cs
│   ├── HotkeyService.cs
│   ├── SettingsService.cs
│   ├── WindowCaptureService.cs
│   ├── WgcCaptureService.cs
│   ├── UtilityService.cs
│   ├── LogService.cs
│   ├── SharedStateService.cs
│   ├── SoundService.cs
│   └── CustomDialogService.cs
├── Styles/              # UI样式和窗口基类
│   ├── BaseWindow.cs
│   ├── CustomWindowStyles.xaml/cs
│   ├── ControlStyles.xaml
│   ├── Colors.xaml
│   └── WindowTemplateConverter.cs
├── ViewModels/          # 视图模型
│   ├── MainViewModel.cs
│   └── HotkeyViewModel.cs
├── Views/               # 用户界面
│   ├── MainPage.xaml/cs
│   ├── SettingsPage.xaml/cs
│   ├── LogPage.xaml/cs
│   └── Hotkey*Dialog.xaml/cs
├── Sounds/              # 嵌入式音效资源
└── [Entry Points]       # 应用程序入口
    ├── App.xaml/cs
    ├── MainWindow.xaml/cs
    └── AssemblyInfo.cs
```

## 🏛️ 架构设计

### MVVM 架构

项目严格遵循 MVVM (Model-View-ViewModel) 模式：

- **Models**：纯数据类，包含业务实体
- **Views**：XAML 界面，专注于显示逻辑
- **ViewModels**：连接 View 和 Model，处理 UI 逻辑和状态管理
- **Services**：独立的业务逻辑服务，通过依赖注入注入到 ViewModel

### 核心服务架构

#### SharedStateService
集中管理应用程序全局状态，使用 `ObservableProperty` 支持数据绑定。

#### CaptureService
控制录制生命周期，协调窗口捕获和视频生成流程。

#### WindowCaptureService + WgcCaptureService
- `WindowCaptureService`：传统的 Win32 API 窗口枚举和管理
- `WgcCaptureService`：基于现代 WGC API 的高性能捕获实现（未使用）

#### SettingsService
使用 JSON 格式持久化用户配置，支持实时保存和加载。

### 依赖注入配置

在 `App.xaml.cs` 中配置所有服务：

```csharp
private void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<SharedStateService>();
    services.AddSingleton<LogService>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<WindowCaptureService>();
    services.AddSingleton<WgcCaptureService>();
    services.AddSingleton<UtilityService>();
    services.AddSingleton<CaptureService>();
    services.AddSingleton<HotkeyService>();
    services.AddSingleton<MainViewModel>();
    services.AddSingleton<HotkeyViewModel>();
}
```

## 🔧 核心功能实现

### 1. 高效帧差检测算法

```csharp
// 在 WindowCaptureService 中实现
private bool HasFrameChanged(Mat currentFrame, Mat previousFrame)
{
    if (previousFrame == null) return true;
    
    // 转换为灰度图像进行比较
    using var grayCurrent = currentFrame.CvtColor(ColorConversionCodes.BGR2GRAY);
    using var grayPrevious = previousFrame.CvtColor(ColorConversionCodes.BGR2GRAY);
    
    // 计算帧差
    using var diff = grayCurrent.AbsDiff(grayPrevious);
    
    // 统计非零像素比例
    var nonZeroCount = Cv2.CountNonZero(diff);
    var totalPixels = diff.Rows * diff.Cols;
    var changeRatio = (double)nonZeroCount / totalPixels;
    
    return changeRatio > 0.01; // 1% 变化阈值
}
```

### 2. Windows Graphics Capture 集成 （未使用）

使用现代 WGC API 实现硬件加速的窗口捕获：

```csharp
// 在 WgcCaptureService 中
public async Task<Mat?> CaptureWindowAsync(IntPtr hwnd)
{
    var captureItem = CreateCaptureItemForWindow(hwnd);
    var framePool = CreateDirect3DDeviceFramePool();
    
    // 设置帧捕获回调
    framePool.FrameArrived += OnFrameArrived;
    
    var session = framePool.CreateCaptureSession(captureItem);
    session.StartCapture();
    
    // 等待帧完成...
}
```

### 3. 全局热键系统

使用 Win32 API 注册系统级热键：

```csharp
// 在 HotkeyService 中
private void RegisterHotkey(HotkeyModel hotkey)
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

### 4. 自定义窗口样式系统

实现现代化的窗口外观和行为：

```csharp
// 在 BaseWindow.cs 中
public class BaseWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyCustomChrome();
        EnableWindowTransparency();
        SetupWindowBehavior();
    }
}
```

## 🛠️ 开发环境搭建

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
4. 调试输出会显示在 Visual Studio 的 Output 窗口中

### 依赖包更新

```bash
# 更新所有 NuGet 包
dotnet add package CommunityToolkit.Mvvm --version latest
dotnet add package OpenCvSharp4 --version latest
dotnet add package WPF-UI --version latest
```

## 🔍 关键设计决策

### 1. 为什么选择 OpenCvSharp？

- 成熟的图像处理库，支持多种编解码器
- 跨平台兼容性（虽然项目主要针对 Windows）
- 丰富的图像处理算法，便于后期功能扩展
- 性能优秀，支持多线程处理

### 2. 混合使用传统 Win32 API 和 WGC API

- **Win32 API**：用于窗口枚举、热键注册等系统级操作
- **WGC API**：用于高性能的窗口内容捕获
- 这种混合策略在兼容性和性能之间取得了平衡

### 3. 自定义窗口样式的实现

为了实现现代化的 UI 效果，项目没有使用系统默认窗口样式，而是：

- 完全自定义窗口的边框、标题栏和控件
- 支持窗口透明效果和圆角设计
- 实现了一致的深色主题

### 4. 内存管理策略

在大量图像处理场景中，特别注意内存管理：

- 所有 `Mat` 对象都使用 `using` 语句确保及时释放
- 实现了 `IDisposable` 接口的服务正确处理资源清理
- 限制日志条目数量，防止内存泄漏

## 🚧 扩展开发指南

### 添加新的捕获模式

1. 在 `Services` 目录下创建新的服务类
2. 实现 `IWindowCaptureProvider` 接口
3. 在 `CaptureService` 中注册新的提供程序
4. 在 UI 中添加配置选项

### 扩展视频编码格式

```csharp
// 在 CaptureService 中添加新的编码器支持
public enum VideoFormat
{
    MP4,
    AVI,
    MKV,
    MOV
}

private FourCC GetVideoCodecFourCC(VideoFormat format)
{
    return format switch
    {
        VideoFormat.MP4 => FourCC.FromString("mp4v"),
        VideoFormat.AVI => FourCC.FromString("xvid"),
        _ => FourCC.FromString("mp4v")
    };
}
```

### 添加新的 UI 页面

1. 在 `Views` 目录下创建新的 `.xaml` 文件
2. 创建对应的 `ViewModel`
3. 在 `MainWindow.xaml` 中添加新的 `TabItem`
4. 在依赖注入容器中注册服务

## 🧪 测试策略

### 单元测试

```bash
# 创建测试项目
dotnet new xunit -n Sai2Capture.Tests

# 运行测试
dotnet test
```

### 关键测试场景

- 配置序列化/反序列化
- 帧差检测算法正确性
- 热键注册和取消注册
- 视频生成流程完整性

### 性能测试

- 内存使用情况监控
- 大型窗体捕获性能
- 长时间录制稳定性

## 📊 性能优化建议

### 1. 图像处理优化

- 使用适当的图像格式（减少内存拷贝）
- 实现帧缓存池，避免频繁的内存分配
- 考虑使用 GPU 加速的图像处理

### 2. UI 响应性

- 确保所有耗时操作都在后台线程执行
- 使用 `Dispatcher` 正确更新 UI
- 实现进度指示器提升用户体验

### 3. 资源管理

- 及时释放 OpenCV 相关资源
- 监控内存使用情况
- 实现资源重用机制

## 🐛 常见开发问题

### 1. WGC API 权限问题

确保应用具有必要的权限：
- Windows 10/11 桌面应用权限
- 屏幕录制权限（在某些企业环境中）

### 2. OpenCV 版本兼容性

不同版本的 OpenCvSharp 可能有 API 变化：

```xml
<!-- 使用特定版本确保稳定性 -->
<PackageReference Include="OpenCvSharp4" Version="4.8.0.20230708" />
```

### 3. 热键冲突检测

实现热键冲突检测逻辑：

```csharp
private bool ValidateHotkeyCombination(HotkeyModel newHotkey)
{
    return !_registeredHotkeys.Values.Any(h => 
        h.Key == newHotkey.Key && 
        h.Modifiers == newHotkey.Modifiers);
}
```

## 📝 贡献指南

### 代码规范

- 使用 C# 12 特性（在 .NET 8 环境下）
- 遵循 Microsoft C# 编码约定
- 为公共成员提供 XML 文档注释
- 使用 `var` 进行局部变量类型推断

### 提交规范

```
feat: 添加新功能
fix: 修复 bug
docs: 更新文档
style: 代码格式调整
refactor: 代码重构
test: 添加或修改测试
chore: 构建过程或辅助工具的变动
```

### Pull Request 流程

1. Fork 项目仓库
2. 创建功能分支 (`git checkout -b feature/amazing-feature`)
3. 提交更改 (`git commit -m 'Add some amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 创建 Pull Request

## 🔗 相关资源

- [WPF-UI Documentation](https://wpfui.lepo.co/)
- [OpenCvSharp Documentation](https://shimat.github.io/opencvsharp/)
- [Windows Graphics Capture API](https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

---

**Happy Coding!** 🚀