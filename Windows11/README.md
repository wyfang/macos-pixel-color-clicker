# Windows 11 版本

这是屏幕颜色触发点击器的 Windows 11 原生版本，使用 C#、.NET 10、Windows Forms 和 Win32 API 实现。

## 功能

- 移动鼠标后按 `Enter` 确认监控位置。
- 匹配一个或多个目标颜色。
- 从屏幕直接吸取颜色。
- 每个颜色可设置独立点击延时。
- 可调 RGB 颜色容差。
- 检测像素颜色变化。
- 点击监控位置或当前鼠标位置。
- 3 秒启动倒计时。
- `Esc` 全局紧急停止。

## 本地构建

在 Windows 11 上安装 .NET 10 SDK 后运行：

```powershell
dotnet publish PixelColorClicker.csproj -c Release -r win-x64 --self-contained true
```

## GitHub 自动构建

仓库中的 `Build Windows EXE` 工作流会在 GitHub 的 Windows 构建机上生成自包含的单文件：

```text
PixelColorClicker.exe
```

在仓库的 **Actions → Build Windows EXE** 页面打开成功的构建，即可下载该文件。

## 注意事项

程序默认以普通用户权限运行。根据 Windows UIPI 安全机制，普通权限程序无法向以管理员身份运行的软件发送点击；遇到这种情况时，需要让本程序也以管理员身份运行。
