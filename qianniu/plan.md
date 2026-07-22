# 自动更新机制开发计划

见 [ADR 0005](docs/adr/0005-update-mechanism.md) 了解整体决策和取舍；这里是落地到代码的具体清单。

## 0. 范围边界

- ✅ 启动时异步检查更新、有新版本弹提示、点了就静默下载+安装+自动重启。
- ✅ 托盘右键新增一级菜单"检查更新"，手动触发同一套逻辑，无更新时提示"已是最新版本"。
- ❌ 应用内"回退到某个历史版本"的功能——挪到后端页面，见 [docs/rollback-download-page-requirements.md](docs/rollback-download-page-requirements.md)（我另外写，供你转给后端团队）。
- ❌ 强制更新（不给用户选"暂不"）。
- ❌ 具体下载百分比进度条。
- ❌ "跳过这个版本，下次不再提示"的记忆。

## 0.1 版本号唯一来源

发布新版本时，**只改 [installer/QianNiuBot.iss](installer/QianNiuBot.iss) 里的 `#define AppVersion "X.Y.Z"` 这一行**。编译 `Bot.csproj` 时会自动跑 [tools/SyncVersion.ps1](tools/SyncVersion.ps1)，把这个值换算同步到：
- `AssemblyInfo.cs` 的 `AssemblyVersion`/`AssemblyFileVersion`（格式 `X.Y.Z.0`）
- `StartUp/Params.cs` 的 `Version`（格式 `major*10000+minor*100+patch`，本节第 1 部分的接口契约、下面 `Params.Version` 的比较逻辑都用这个）

之前这三处是各自独立手动维护的常量，已经互相对不上了（`Params.Version` 是 90502，`AssemblyVersion` 是 1.0.0.0，`.iss` 原来是 1.0.0），这次统一之后 `.iss` 的 `AppVersion` 已经改成跟 `Params.Version=90502` 对应的 `9.5.2`，其余两处会在下次编译时自动纠正。

## 1. 接口契约：`GET /update/latest`

跟现有 `TicketApiClient`/`BetterYeahClient` 一样挂在 `http://116.62.102.58`，同样的鉴权方式。

**请求**
```
GET http://116.62.102.58/update/latest
Headers:
  accept: application/json
  x-api-key: <跟现有工单接口一样的 key，具体值找后端要>
```

**响应**
```json
{
  "version": "v9.5.3",
  "downloadUrl": "https://xxx.oss-cn-hangzhou.aliyuncs.com/智能客服助手-Setup-9.5.3.exe",
  "releaseNotes": "修复了xxx问题"
}
```

- `version`：字符串，格式跟 `ShareUtil.ConvertVersionToString` 输出的一致（`v{major}.{minor}.{patch}`，**不补零**——`Params.Version=90503` 转出来是 `v9.5.3` 不是 `v9.05.03`），客户端用 `ShareUtil.ConvertStringToVersion` 转回 int 跟 `Params.Version` 比较（解析时补不补零其实都能识别，但建议跟客户端自己显示的格式保持一致，避免歧义）。
- `downloadUrl`：安装包直链，客户端直接下载，不关心存在哪。
- `releaseNotes`：可选，弹窗里如果有就带上显示，没有就不显示这部分。

## 2. C# 端新增代码

新建 `src/Bot/Update/` 目录（新业务域，不塞进 `ChromeNs`/`Ticket` 这些已有目录）：

- **`UpdateApiClient.cs`**：仿照 [TicketApiClient.cs](src/Bot/Ticket/TicketApiClient.cs) 的结构——`BaseUrl`/`ApiKey` 常量、`GetAsync`/`DownloadAsync`（工单那边已经有现成的 `DownloadAsync(string url)` 可以照抄），解析出 `UpdateInfo { Version, DownloadUrl, ReleaseNotes }`。
- **`UpdateManager.cs`**：
  - `CheckOnStartupAsync()`：调接口 → 解析版本 → 比 `Params.Version` → 有新版本就在 UI 线程弹 `MsgBox.ShowDialogEx`（"发现新版本 {version}，是否立即更新？"，带 releaseNotes）→ 确认就走下载安装流程；网络异常整体 try/catch，只记日志不提示。
  - `CheckManuallyAsync()`：同样的检查逻辑，但失败时要提示（`MsgBox.ShowErrTip`），没有新版本时提示"当前已是最新版本"（`MsgBox.ShowTip`）。
  - `DownloadAndInstallAsync(UpdateInfo info)`：
    1. `WndLoading.ShowWaiting("正在下载并安装更新，程序稍后会自动重启")`
    2. 下载到 `PathEx.TmpPath` 下的一个临时文件名（比如 `更新安装包.exe`）
    3. `Process.Start` 拉起下载好的文件，参数：`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`
    4. 拉起成功后主动退出当前程序（`Application.Current.Shutdown()`，走正常关闭路径释放 Mutex/日志）
    5. 任何一步失败（下载异常、`Process.Start` 抛异常）→ 关掉等待提示、`MsgBox.ShowErrTip` 报错，不退出程序

## 3. 接入点

- [BootStrap.cs](src/Bot/StartUp/BootStrap.cs) 的 `Init()`：加一行 `Task.Run(() => UpdateManager.CheckOnStartupAsync());`（fire-and-forget，不 await，不阻塞后面的 `DeskScanner.LoopScan()` 等步骤）。
- 托盘菜单：
  - [WndNotifyIcon.xaml](src/Bot/AssistWindow/NotifyIcon/WndNotifyIcon.xaml)：在"帮助"和"设置"之间加一个一级 `<forms:ToolStripMenuItem Text="检查更新" Click="btnCheckUpdate_Click" />`。
  - [WndNotifyIcon.xaml.cs](src/Bot/AssistWindow/NotifyIcon/WndNotifyIcon.xaml.cs)：加 `btnCheckUpdate_Click` 调 `UpdateManager.CheckManuallyAsync()`。

## 4. 安装脚本改动

[installer/QianNiuBot.iss](installer/QianNiuBot.iss) 的 `[Setup]` 段加：
```
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes
```
（`Bin\` 子目录那个坑上次已经修过了，这次不用再动 `[Files]`。）

## 5. 需要你/后端确认的收尾项

1. `x-api-key` 用现有工单接口同一个 key，还是要单独发一个新的？
2. `/update/latest` 这个接口路径 OK 吗，还是后端团队想用别的路径规范？

## 6. 确认结果

（等你看过以上细节确认后开始写代码。）
