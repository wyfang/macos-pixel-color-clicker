# Windows 11 版本

这是屏幕颜色触发点击器的 Windows 11 原生版本，使用 C#、.NET 10、Windows Forms 和 Win32 API 实现。

## 功能

- 移动鼠标后按 `Enter` 确认监控位置。
- 匹配一个或多个目标颜色。
- 从屏幕直接吸取颜色。
- 每个颜色可独立设置点击延时、连续点击次数和点击间隔。
- 可调 RGB 颜色容差。
- 检测区域颜色变化，采样区域支持 `1×1` 至 `10×10`。
- 像素变化模式可设置点击延时、连续点击次数和点击间隔。
- 点击次数默认为 1 次，可设置 1–100 次；延时及间隔范围为 0–60000 毫秒。
- 点击监控位置或当前鼠标位置。
- 启动倒计时可设置为 0–60 秒，默认 3 秒。
- 可在 `F6`–`F12` 中设置全局开始/停止快捷键，默认 `F8`。
- 自动保存并恢复监控位置、目标颜色列表以及两种模式的全部参数。
- `Esc` 全局紧急停止，并取消尚未执行的延时点击和连续点击。
- 窗口顶部醒目显示当前权限状态；普通权限时可点击“以管理员身份重启”。

## 本地构建

在 Windows 11 上安装 .NET 10 SDK 后运行：

```powershell
dotnet publish PixelColorClicker.csproj -c Release -r win-x64 --self-contained true
```

## GitHub 自动构建

仓库中的 `Build Windows EXE` 工作流会在 GitHub 的 Windows 构建机上生成便携式文件夹：

```text
PixelColorClicker-Windows11-x64/
├── PixelColorClicker.exe
└── settings.json
```

在仓库的 **Actions → Build Windows EXE** 页面打开成功的构建，即可下载该文件夹产物。

## 注意事项

程序默认以普通用户权限运行。根据 Windows UIPI 安全机制，普通权限程序无法向以管理员身份运行的软件发送点击；遇到这种情况时，点击窗口顶部的“以管理员身份重启”，并在 Windows UAC 提示中确认。

没有强制程序每次都申请管理员权限，因为监控普通软件时不需要提升权限，也可以避免每次启动都显示 UAC。

程序采用便携式目录：`PixelColorClicker.exe` 与 `settings.json` 放在同一个文件夹，所有设置直接保存在 EXE 旁边的 JSON 文件中。请把整个文件夹放在当前用户可写的位置，不要只移动 EXE，也不建议放入受保护的 `Program Files` 目录。
