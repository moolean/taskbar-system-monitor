# Taskbar System Monitor

一个轻量、无第三方依赖的 Windows CPU 与内存任务栏监控小程序。

## 功能

- 在任务栏右侧直接常驻显示 `CPU 12%` 和 `RAM 48%`，无需点开
- 根据负载自动变色：正常、偏高、过高一眼可见
- 自动跟随任务栏的位置与尺寸，支持横向和纵向任务栏
- 通知区图标作为备用入口，悬停可看已用/总内存
- 单击任务栏监控条或双击托盘图标可打开详细面板
- 右键菜单可快速打开任务管理器
- 第一次运行自动加入当前用户的开机启动项
- 可随时隐藏任务栏数值条，或关闭/重新开启开机自启
- 单实例运行，重复启动不会出现多个监控器
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
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
.\dist\TaskbarSystemMonitor.exe
```

程序首次运行时会把当前位置写入开机启动项。如果之后移动了 EXE，请在右键菜单中先关闭、再重新开启“开机自动启动”。

## 使用

- 任务栏数值条：CPU 为青色，内存为紫色；负载偏高显示橙色，过高显示红色。
- 左键单击任务栏数值条：打开详细监控面板。
- 右键单击任务栏数值条：打开设置菜单。
- 如果不想显示数值条，可取消勾选“在任务栏直接显示数值”，托盘监控仍会继续。

## 卸载

```powershell
Set-ExecutionPolicy -Scope Process Bypass
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
