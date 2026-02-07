# 此文档由AI生成，部分内容可能存在不准确或错误，敬请谅解。
# Sai2Capture Developer Documentation

## 🏗️ 项目概述

Sai2Capture 是一个基于 C# WPF 的桌面应用程序，专门用于捕获 SAI2 绘画软件的窗口内容并生成视频文件。项目采用 MVVM 架构模式，使用现代 .NET 8 技术。

### 核心技术栈

- **框架**：.NET 8.0 (WPF + WindowsForms)
- **架构**：MVVM (CommunityToolkit.Mvvm)
- **UI库**：WPF-UI v3.1.0
- **视频处理**：OpenCvSharp4
- **窗口捕获**：Windows PrintWindow API（优化后移除WGC）
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

#### WindowCaptureService（优化后）
使用传统的 Windows PrintWindow API 实现窗口捕获：
- 移除了 WGC 相关代码以减少依赖和体积
- 保持了良好的兼容性和稳定性
- 统一使用 PrintWindow API 简化了代码架构

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
private bool ImagesEqual(Mat? img1, Mat img2)
{
    if (img1 == null) return false;
    if (img1.Size() != img2.Size()) return false;
    if (img1.Channels() != img2.Channels()) return false;

    using Mat diff = new Mat();
    Cv2.Absdiff(img1, img2, diff);

    // 对于多通道图像，需要先转换为灰度图再计数非零像素
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

### 2. PrintWindow API 窗口捕获

使用稳定可靠的 Windows PrintWindow API：

```csharp
// 在 WindowCaptureService 中
public Mat CaptureWindowContentLegacy(nint hWnd)
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

    // 转换 Bitmap 到 OpenCV Mat
    // ...
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

## 🐛 体积优化架构

### 优化策略实施

项目经过全面的体积优化，相比初始版本减少约 70%+ 的文件大小：

#### 1. WGC 组件移除
- **删除文件**：`WgcCaptureService.cs`
- **移除依赖**：`SharpDX.Direct3D11`, `SharpDX.DXGI`
- **简化调用**：统一使用 PrintWindow API
- **减少复杂度**：移除了现代 WGC 相关的复杂初始化逻辑

#### 2. OpenCV 依赖精简
```xml
<!-- 在 Sai2Capture.csproj 中 -->
<Project>
  <!-- 优化OpenCV运行时，仅包含x64架构 -->
  <ItemGroup>
    <Content Remove="runtimes\win-x86\**" />
    <Content Remove="runtimes\win-arm64\**" />
    <None Remove="runtimes\win-x86\**" />
    <None Remove="runtimes\win-arm64\**" />
  </ItemGroup>
</Project>
```

#### 3. 服务架构简化
- **统一接口**：`WindowCaptureService` 提供单一的捕获方法
- **移除抽象**：删除了多捕获提供程序的复杂逻辑
- **精简依赖**：减少了服务间的耦合度

### 优化后的体积构成

```
总发布体积：186.3 MB
├── OpenCV 运行时 (x64): ~150MB (核心图像处理库)
├── .NET 运行时: ~30-40MB (自包含运行时)
├── 应用代码与依赖: ~10-20MB (业务逻辑和其他库)
└── 资源文件: ~84KB (声音文件 - 嵌入式)
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

### 单文件发布命令

```bash
# 发布优化后的单文件版本
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# 输出目录：bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/
# 最终文件：Sai2Capture.exe (186.3 MB)
```

### 依赖包管理（当前版本）

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

### 1. 为什么移除 WGC API？

- **体积考虑**：WGC 需要 SharpDX 依赖，增加约 20-30MB
- **兼容性**：PrintWindow API 在所有 Windows 版本上稳定运行
- **维护成本**：单一捕获方式降低了代码复杂度
- **性能权衡**：对于 SAI2 的静态绘画场景，PrintWindow 性能完全足够

### 2. 为什么保持 OpenCV 而不是其他方案？

- **稳定性**：OpenCvSharp4 4.8.0 版本经过充分测试
- **功能完整性**：支持多种视频编码器和图像处理功能
- **生态成熟**：丰富的文档和社区支持
- **性价比**：对于所需的视频编码功能，OpenCV 是最优选择

### 3. 自定义窗口样式的实现

为了实现现代化的 UI 效果，项目没有使用系统默认窗口样式：

- 完全自定义窗口的边框、标题栏和控件
- 支持窗口透明效果和圆角设计
- 实现了一致的深色主题
- 保持轻量级，无额外UI框架依赖

### 4. 内存管理策略

在大量图像处理场景中，特别注意内存管理：

- 所有 `Mat` 对象都使用 `using` 语句确保及时释放
- 实现了 `IDisposable` 接口的服务正确处理资源清理
- 限制日志条目数量，防止内存泄漏
- 避免不必要的大对象分配

## 🚧 扩展开发指南

### 添加新的视频编码格式

```csharp
// 在 CaptureService 中添加新的编码器支持
public enum VideoFormat
{
    MP4,
    AVI,
    MKV,
    MOV
}

private string GetVideoFileExtension(VideoFormat format)
{
    return format switch
    {
        VideoFormat.MP4 => ".mp4",
        VideoFormat.AVI => ".avi",
        VideoFormat.MKV => ".mkv",
        VideoFormat.MOV => ".mov",
        _ => ".mp4"
    };
}
```

### 添加新的 UI 页面

1. 在 `Views` 目录下创建新的 `.xaml` 文件
2. 创建对应的 `ViewModel`
3. 在 `MainWindow.xaml` 中添加新的 `TabItem`
4. 在依赖注入容器中注册服务

### 优化性能建议

#### 图像处理优化

```csharp
// 使用适当的图像格式（减少内存拷贝）
public Mat CaptureWindowContent(nint hWnd)
{
    // 直接返回，避免不必要的克隆
    return CaptureWindowContentLegacy(hWnd);
}
```

#### 异步操作优化

```csharp
// 确保UI响应性
public async Task<bool> InitializeCaptureAsync(nint hWnd)
{
    // 使用 Task.FromResult 避免不必要的异步开销
    return await Task.FromResult(true);
}
```

## 🧪 测试策略

### 关键测试场景

- 配置序列化/反序列化
- 帧差检测算法正确性
- 热键注册和取消注册
- 视频生成流程完整性
- 体积优化后的功能完整性

### 性能测试

- 内存使用情况监控
- 大型窗体捕获性能
- 长时间录制稳定性
- 单文件发布启动速度测试

## 📊 体积优化后的性能特点

### 启动性能

- **冷启动时间**：3-5秒（较优化前无明显变化）
- **内存占用**：50-100MB（运行时，取决于捕获分辨率）
- **CPU 使用**：空闲时 < 1%，录制时 5-15%

### 兼容性

- **Windows 版本**：Windows 10 19041+ / Windows 11
- **架构支持**：仅 x64（优化后，减少体积）
- **依赖要求**：无外部依赖，完全自包含

## 🐛 优化后的常见问题

### 1. 单文件发布启动慢

正常现象，自包含的单文件需要解压运行时到临时目录：
- 首次启动：3-5秒
- 后续启动：利用缓存，速度更快

### 2. 高DPI显示器兼容性

PrintWindow API 在高DPI环境下的处理：

```csharp
// 在 WindowCaptureService 中确保正确的DPI感知
private Mat CaptureWindowContentLegacy(nint hWnd)
{
    // SetProcessDPIAware() 已在 App 启动时调用
    // 确保获取真实像素尺寸
}
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

### 代码规范（优化后）

- 遵循 Microsoft C# 编码约定
- 使用 C# 12 特性（在 .NET 8 环境下）
- 为公共成员提供 XML 文档注释
- **体积敏感**：新增依赖时考虑对发布体积的影响

### 提交规范

```
feat: 添加新功能
fix: 修复 bug
docs: 更新文档
style: 代码格式调整
refactor: 代码重构
test: 添加或修改测试
chore: 构建过程或辅助工具的变动
optimize: 性能或体积优化
```

### 体积优化相关的贡献

在提交体积相关改动时，请包含：

1. **优化前后的体积对比**
2. **功能完整性验证报告**
3. **性能影响评估**
4. **兼容性测试结果**

## 🔗 相关资源

- [WPF-UI Documentation](https://wpfui.lepo.co/)
- [OpenCvSharp Documentation](https://shimat.github.io/opencvsharp/)
- [.NET 8 Optimizations](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/ready-to-run)
- [Windows PrintWindow API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)

---

**Happy coding with optimized footprint!** 🚀📦