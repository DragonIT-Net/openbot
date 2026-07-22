# 自动更新机制：关闭旧进程/重启交给 Inno Setup，而不是自己写

程序目前没有任何更新机制，装好之后要么手动换 exe，要么走一遍完整的重新安装。这次要做到：启动时和托盘右键都能检查更新，发现新版本弹"是否立即更新"，点了就自动下载、自动装、自动重启，全程不需要用户手动下载文件（回退到旧版本除外，见下文范围边界）。

## Considered Options

**关闭旧进程 + 替换文件/装包 + 重新拉起新进程，这一整套谁来做：**

- **C# 自己写等待/替换逻辑**：下载完新文件后，自己关掉当前进程，用一个外部小工具或者批处理脚本等主进程完全退出、文件锁释放后再替换文件、再拉起新 exe。能精确控制每一步，但要自己处理"等多久才算主进程真退出了""替换到一半失败怎么办""要不要发一个单独的 updater.exe 辅助进程"这些时序和失败恢复问题，本质是在重新发明 Inno Setup 已经做好的事。
- **交给 Inno Setup 内置的 `CloseApplications`/`RestartApplications`（采纳）**：Inno Setup 6 自带"检测到目标文件被某进程占用 → 自动关闭该进程 → 装完文件 → 用原来的方式重新拉起该进程"这一整套能力，是安装器领域被大量验证过的标准做法。C# 端只需要下载安装包、用静默参数拉起它，然后主动退出自己（让 Inno 的强制关闭只是一个保险，不是主要手段）。

## 设计要点

- 新增 Python 后端接口 `GET /update/latest`（挂在现有 `http://116.62.102.58`，跟 `TicketApiClient`/`BetterYeahClient` 一样的 `x-api-key` 鉴权方式），返回最新版本号 + 一个下载链接；安装包实际存在哪（OSS 还是别的）是后端的事，客户端不关心。
- 版本比较直接用现有的 `Params.Version`（int）和 `ShareUtil.ConvertStringToVersion`，不引入新的版本号格式。
- 触发时机：启动时在 [BootStrap.cs](src/Bot/StartUp/BootStrap.cs) 里异步检查一次（不阻塞其它启动步骤）；托盘右键新增一个**一级菜单项**"检查更新"，手动触发同一套检查逻辑。
- 每次启动都问，不做"跳过某个版本"的记忆——面向的是客服人员、装机量不大，没必要为了少弹一次窗口去维护一份本地"已忽略版本"状态。
- 这一期只做可选更新，没有"强制更新、不给点否"的场景。
- 更新过程中的界面反馈复用现成的 `WndLoading.ShowWaiting(tip)`，不做具体下载百分比。
- 用户确认更新后：C# 下载安装包到本地临时目录 → 用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS` 拉起它 → 自己主动退出（释放 Mutex、关日志）。剩下强制关进程、装文件、重新拉起全部交给 Inno Setup。
- 失败处理：启动时自动检查失败→静默记日志不提示；手动检查失败→提示重试；下载/拉起安装包失败→提示错误且不退出程序，继续用当前版本。
- 回退到旧版本本次完全不做，挪到后端一个"上传安装包 + 选版本，客服自己上去下载"的页面，见 [docs/rollback-download-page-requirements.md](docs/rollback-download-page-requirements.md)。

## Consequences

- `.iss` 脚本的 `[Setup]` 段要加 `CloseApplications=force`、`RestartApplications=yes`（以及配套的 `CloseApplicationsFilter`），往后每次改安装脚本都要留意别把这两行删掉，否则静默更新会在"文件被占用"这一步直接失败。
- 依赖 Inno Setup 的重启行为是"用原来的可执行文件路径 + 无参数重新拉起"，跟现在正常双击快捷方式启动完全一致，不需要额外处理命令行参数透传。
- `Params.cs` 里原来没用上的 `KeepInstalledVersionsCount` 常量这次依然不接上，等以后真做应用内回退再说。
