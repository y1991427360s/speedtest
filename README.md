# Windows 悬浮网速监控

这是一个使用 C# + WinForms 实现的 Windows 原生悬浮网速监控工具，不再依赖 Python、PyInstaller 或 Electron。

程序启动后会在屏幕右上角显示一个小型悬浮窗，实时展示下载/上传速度、CPU 使用率、内存使用率和 GPU 0 使用率：

```text
↓ 1.25 MB/s
↑ 256 KB/s
CPU 12%
内存 46%
GPU0 8%
```

## 功能

- Windows 10 / Windows 11
- C# + WinForms
- 默认位于屏幕右上角
- 默认置顶，可通过右键菜单切换
- 无边框
- 深色圆角半透明背景
- 每 1 秒刷新网速
- 每 1 秒刷新 CPU、内存、GPU 0 使用率
- 自动单位换算：KB/s、MB/s
- 支持鼠标左键拖动位置
- 拖动后自动保存位置到 `config.json`
- 右键菜单：置顶、透明度、开机自启、关于、退出
- 支持 0% / 20% / 40% / 60% / 80% 透明度
- 支持暗色 / 亮色模式切换
- 支持可选开机自启
- 网速读取失败时显示“网速读取失败”，不会直接崩溃

## 网速计算方式

程序使用 .NET 自带的 `System.Net.NetworkInformation.NetworkInterface`：

- 获取所有 `OperationalStatus.Up` 的网卡
- 排除 `Loopback` 和 `Tunnel`
- 读取 `IPv4InterfaceStatistics.BytesReceived`
- 读取 `IPv4InterfaceStatistics.BytesSent`
- 每秒计算累计字节差值并换算为速度

## 系统资源读取方式

- CPU：使用 Windows `GetSystemTimes` 计算总 CPU 使用率
- 内存：使用 Windows `GlobalMemoryStatusEx` 读取物理内存使用率
- GPU 0：读取 Windows `GPU Engine` 性能计数器中 `phys_0` 的 `Utilization Percentage`

## 环境要求

开发编译需要：

- .NET 8 SDK

运行 framework-dependent 发布版本需要：

- .NET 8 Desktop Runtime

如果用户电脑运行时报缺少 .NET，请安装 Microsoft 官方的 `.NET 8 Desktop Runtime`。

## 开发运行

在项目目录打开 PowerShell：

```powershell
dotnet run -c Release
```

## 编译

```powershell
dotnet build -c Release
```

编译后的文件位于：

```text
bin\Release\net8.0-windows\
```

## 发布小体积 exe

推荐使用 framework-dependent 发布，不把 .NET 运行时打进程序：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o publish
```

发布后的主程序位于：

```text
publish\SpeedMonitor.exe
```

这个 exe 通常只有约 `140 KB - 200 KB`。同目录还会有一个 `SpeedMonitor.dll` 和少量配置文件，整体通常远小于 Python PyInstaller 的几十 MB。

## self-contained 和 framework-dependent 区别

framework-dependent：

- 推荐默认使用
- 不包含 .NET 运行时
- exe 很小
- 用户电脑需要安装 `.NET 8 Desktop Runtime`

self-contained：

- 会把 .NET 运行时一起发布
- 用户电脑不需要额外安装 .NET
- 体积会明显变大，通常几十 MB

self-contained 发布命令：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish-self-contained
```

除非明确需要免安装运行环境，否则不建议使用 self-contained。

## 为什么比 Python PyInstaller 小

PyInstaller 打包 Python GUI 程序时，需要把 Python 解释器、PySide6/Qt 运行库、psutil 以及大量依赖文件一起打进发布包，所以单文件 exe 很容易超过 40 MB。

这个版本使用 WinForms 和 .NET 自带网络 API。framework-dependent 发布时不携带运行时，只发布应用本身，因此主 exe 可以保持在几百 KB 以内。

## 项目结构

```text
speedtest/
├─ SpeedMonitor.csproj
├─ Program.cs
├─ SpeedMonitorForm.cs
├─ config.json
├─ assets/
│  └─ fast.ico
└─ README.md
```
