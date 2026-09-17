# 运行与维护结构

## 生命周期

Windows 登录触发器 → 任务计划程序 → 已安装的监控器 EXE → WinForms 消息循环。

安装 / 启动脚本完成后即可退出。任务动作直接指向 EXE，不通过 Codex、终端、循环脚本或网络服务。任务使用 InteractiveToken 和 LeastPrivilege，只有该用户已登录的桌面会话才显示横条，不接收或保存密码。单实例既有 Windows 任务的 IgnoreNew，也有程序内命名互斥量。

## 单一职责

| 入口 / 模块 | 职责 |
| --- | --- |
| `build.ps1` | 找到系统编译器、编译全部源码、原子替换构建产物；不改自启，不覆盖运行中的便携 EXE |
| `test.ps1` | 脚本解析与路径验证、应用自检；可选真实桌面回归。CI 复用相同入口 |
| `install.ps1` | 构建验证 → 安全停止 → 单向迁移旧配置 → 备份 / 校验复制 → 注册任务 → 验证启动；失败恢复上一份 EXE |
| `start.ps1` / `stop.ps1` | 通过计划任务启动、或经 IPC 保存并退出；启动幂等，停止不取消下次登录触发 |
| `status.ps1` | 只读版本、PID、任务状态、自启与运行限制，不输出工作笔记或登录凭据 |
| `uninstall.ps1` | 安全停止、移除本产品任务，逐个删除明确命名的产品文件；默认不删设置 |
| `scripts/MonitorTools.psm1` | 共享路径、限时维护命令、精确进程匹配、复制校验与独立启动；不持久驻留 |
| `src/Startup.cs` | 唯一的任务定义 / 注册 / 开关实现；托盘和脚本维护命令复用，旧 Run 项在新注册成功后移除 |
| `src/AppPaths.cs` | 稳定的用户数据路径，旧配置只复制一次，不覆盖现有新配置 |
| `src/MonitorContext.cs` | UI 生命周期、采样协调、保存；Shell / 工作项的 UI 行为分别在对应窗口中 |
| `scripts/Test-CodexConnection.ps1` | 复用应用的只读额度诊断，不维护另一套与桌面进程绑定的代理协议 |

## 数据与更新约束

正式安装和数据目录是 `%USERPROFILE%\.local\TaskbarSystemMonitor`。不使用宿主应用可虚拟化的 AppData 路径作为正式 EXE 的任务动作目标，也不把宿主私有包缓存路径写入任务。

3.7.0 升级时，安装脚本在旧环境仍可见旧配置的情况下复制 `settings.xml`，验证哈希并保留原文件。程序本身也提供缺少新配置时的一次性迁移；两条路径都不会覆盖已存在的新配置。旧版结构清理继续由既有迁移函数处理。新旧副本不双向同步。

构建 / 测试失败发生在停止正式实例之前。升级保留 `TaskbarSystemMonitor.previous.exe`；复制经过 SHA-256 校验。安全退出会先保存工作笔记并释放 AppBar 预留空间，保存失败则中止升级，不强杀用户界面。短命维护 / 测试子进程有超时清理，不能被误认为长期常驻实例。

## 运行策略与边界

- 任务执行时限 `PT0S`，不会在默认的 72 小时后被调度器停止；电池、网络、空闲状态不作为停止条件。
- 失败重试间隔 1 分钟、最多 3 次；正常退出码 0 不触发失败重试。主动退出与禁止登录启动是不同操作。
- Windows 休眠 / 注销 / 关机不是持续工作的状态；程序不唤醒设备，也不阻止关机。
- `.NET Framework 4.8` 为运行前提。企业限制创建任务、损坏的运行库、文件权限问题会显式失败；不修改系统安全策略来绕过限制。
- 原有边界不变：主屏单行 AppBar、默认不置顶、公网查询需主动开启。额度服务不可用不影响本地资源和笔记。

接口依据：[任务注册与交互式令牌](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertask)、[执行时限](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-executiontimelimit)、[失败重试次数](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-restartcount)。
