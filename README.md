# Taskbar System Monitor

一个轻量、无第三方依赖的 Windows CPU 与内存托盘监控小程序。

## 功能

- 通知区图标用两根彩色柱实时表示 CPU（青色）和内存（紫色）占用
- 鼠标悬停显示 CPU 百分比、内存百分比和已用/总内存
- 双击托盘图标打开深色监控面板
- 右键菜单可快速打开任务管理器
- 第一次运行自动加入当前用户的开机启动项
- 右键菜单可随时关闭或重新开启开机自启
- 单实例运行，重复启动不会出现多个托盘图标
- 仅调用 Windows 系统 API，无常驻服务、无网络请求、无遥测

## 系统要求

- Windows 10 / Windows 11
- .NET Framework 4.8（Windows 10/11 通常已内置或可由系统更新安装）

## 快速使用

### 方式一：一键构建并安装

在 PowerShell 中进入项目目录，运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install.ps1
```

脚本会：

1. 使用 Windows 自带的 C# 编译器构建程序；
2. 将程序复制到 `%LOCALAPPDATA%\TaskbarSystemMonitor`；
3. 写入当前用户的开机启动项；
4. 立即启动程序。

不需要管理员权限。

### 方式二：只构建

```powershell
.\build.ps1
.\dist\TaskbarSystemMonitor.exe
```

程序首次运行时会把当前位置写入开机启动项。如果之后移动了 EXE，请在托盘右键菜单中先关闭、再重新开启“开机自动启动”。

## 卸载

```powershell
.\uninstall.ps1
```

该脚本会删除当前用户的开机启动项和 `%LOCALAPPDATA%\TaskbarSystemMonitor` 安装目录。

## 开发

源码位于 `src/TaskbarSystemMonitor.cs`。本地 `build.ps1` 使用 Windows 自带的 .NET Framework C# 编译器，因此不要求安装 Visual Studio 或 .NET SDK。

也可以使用 Visual Studio / .NET SDK 打开 `TaskbarSystemMonitor.csproj` 构建。

## 隐私

程序只读取本机 CPU 与物理内存统计信息。它不会访问网络、收集数据或上传遥测。

## 许可证

[MIT](LICENSE)
