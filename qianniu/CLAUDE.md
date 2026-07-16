# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目简介

千牛客服机器人（智能辅助）：一个 Windows 桌面程序，为淘宝千牛客服工作台提供 AI 自动回复、催付/核对卡片、自动发货、转接等能力。核心思路是"外挂 + 注入 + UI 自动化"三合一：不修改千牛本体，而是通过向千牛内嵌网页注入脚本、WebSocket 通信、以及对千牛窗口做 UI Automation 操作来实现自动化。

详见 [README.md](README.md)。

## 构建与运行

这是 .NET Framework 4.8 的 Windows 桌面（WPF/WinForms）项目，只能在 Windows 上用 Visual Studio / MSBuild 构建，此仓库当前开发环境（macOS）无法直接编译或运行。

- 解决方案文件：[src/Bot.sln](src/Bot.sln)，包含三个项目：
  - `BotLib` — 通用基础库（无业务依赖）
  - `DbEntity` — 数据实体/DTO 定义（依赖 BotLib）
  - `Bot` — 主程序，WPF 可执行文件（`OutputType=WinExe`，`RootNamespace=Bot`），依赖前两者
- 在 Windows 上构建：用 Visual Studio 打开 `src/Bot.sln`，或执行 `msbuild src/Bot.sln /p:Configuration=Release /p:Platform=x64`（Debug 平台仅支持 `Any CPU`/`x64`，Release 支持 `Any CPU`/`x64`/`x86`，见 [src/Bot.sln](src/Bot.sln)）。
- 依赖通过老式 `packages.config` + NuGet 管理（每个项目一个 `packages.config`），不是 PackageReference/SDK-style。
- 没有自动化测试项目，也没有 CI 配置；验证方式主要是在 Windows 上实际运行千牛 + 本程序联调。
- 程序运行时会请求管理员权限自动重启自身（见 [src/Bot/StartUp/StartUp.cs](src/Bot/StartUp/StartUp.cs)），并用命名 Mutex `qnrobot` 保证单实例。

## 整体架构

### 三层项目依赖
```
Bot (WPF 主程序) → DbEntity (实体/DTO) → BotLib (通用工具库)
```
- **BotLib**：不含业务逻辑的通用能力：日志（`Log`/`LogWriter`）、SQLite 封装（`BotLib/Db/Sqlite`）、加解密（`BotLib/Cypto`）、文件序列化、集合/扩展方法（`BotLib/Extensions` 下大量 `xXxx` 风格扩展方法，如 `xSafeForEach`、`xSafeForEach`、`xSaveParam`）、WPF 辅助（`BotLib/Wpf`）。
- **DbEntity**：所有落库实体（继承自 `EntityBase`，见 [src/DbEntity/Core/EntityBase.cs](src/DbEntity/Core/EntityBase.cs)）与千牛接口返回的 Response DTO（`DbEntity/Response/*`），按业务域分目录（`Account`、`Trade`、`Sync`、`Goods`、`Login`、`WorkMode` 等）。
- **Bot**：主程序，按职责分目录：
  - `StartUp/` — 程序入口（`StartUp.Main`）、启动引导（`BootStrap.Init`）、全局参数（`Params`）
  - `ChromeNs/` — 与千牛内嵌浏览器通信的核心：`QN`（业务门面）、`CDPClient`（协议客户端）、`MyWebSocketServer`（本地 WebSocket 服务端）、`MyOpenAI`（AI 对接）
  - `Automation/` — Win32 API、键鼠模拟、窗口钩子等底层自动化；`Automation/ChatDeskNs/` 是基于 FlaUI 的千牛接待窗口 UI 自动化（`Desk`、`QnAccountFinder` 等）
  - `ControllerNs/` — `DeskScanner`：定时扫描桌面窗口，发现/绑定千牛接待窗口
  - `Common/` — 跨模块工具：`DbHelper`（业务数据存取门面）、`QNInject`（往千牛资源包注入脚本）、`MsgBox`、`Common/Db/HybridHelper`、`Common/Windows/*`（各类提示窗口）
  - `AssistWindow/` — 主 UI：悬浮助手窗口、托盘图标菜单、右侧面板（话术/商品/机器人/订单/优惠券等 Tab）
  - `Options/` — 设置窗口（AI 参数、机器人选项等配置 UI）
  - `Asset/` — 图片等静态资源管理

### 核心工作流程（三条线交织）

1. **脚本注入**：[Bot/Common/QNInject.cs](src/Bot/Common/QNInject.cs) 在程序启动时定位千牛安装路径（读注册表 `aliim` 项）→ 找到资源目录下的 `webui.zip` → 修改其中 `recent.html`，把千牛原有的 `imSupportUrl` 替换成外部脚本 URL，从而让千牛内嵌网页加载本程序需要的注入脚本。注入后需要重启千牛才生效。
2. **WebSocket 通信**：注入的网页脚本与本程序的 `MyWebSocketServer`（[src/Bot/ChromeNs](src/Bot/ChromeNs)）建立 WebSocket 连接；`CDPClient` 负责收发消息、解析千牛页面事件（买家切换、收到新消息、消息中心通知等），再由 `QN` 类做业务封装（发送文本/图片、转接、催付卡片、查询买家信息/订单等，均通过 `cdp.*` 转发给页面执行 JS）。
3. **窗口自动化兜底**：`ControllerNs/DeskScanner` 定时扫描系统窗口，识别千牛接待窗口（`QnAccountFinderFactory`），创建/维护 `Automation/ChatDeskNs/Desk` 实例；当千牛版本较低或 WebSocket 方案不可用时，`QNRpa` 用 FlaUI 做 UI Automation（找控件、模拟输入）来发送消息，`QN.SendTextAsync` 内部按千牛版本号选择走旧 API（`SendTimiMsg`）还是 RPA。

AI 自动回复的主链路：`CDPClient` 收到买家消息 → `QN.Cdp_EvRecieveNewMessage` 解析 → 调 [MyOpenAI.GetAnswer](src/Bot/ChromeNs/MyOpenAI.cs)（基于 `OpenAI` SDK，`BaseUrl`/`ApiKey`/`Model`/`SystemPrompt` 均可在设置里配置，兼容 DeepSeek/通义千问等 OpenAI 协议兼容接口）生成回复 → 按每个"客服-买家"对维护独立的上下文（`buyerChatMessages` 以 `seller#buyer` 为 key）→ 通过 `QN.SendTextAsync` 发送。

### 数据存储

- 业务数据落地用自制的轻量 SQLite ORM（`BotLib/Db/Sqlite/SQLiteHelper` 等），数据库文件在 `data/bot.db`；配置类参数（如 API Key、各类开关）走 `PersistentParams`，落在 `data/params.db`。
- 所有落库实体继承 [DbEntity.EntityBase](src/DbEntity/Core/EntityBase.cs)：内置 `EntityId`（主键）、`ModifyTick`（增量同步用时间戳）、`DbAccount`（按千牛账号隔离数据）、`IsDeleted`（软删除）、只读克隆（`Clone`/`SetReadOnly`）语义 —— 修改数据前必须先 `Clone(false)`，直接改只读实例会抛异常。
- [Bot/Common/DbHelper.cs](src/Bot/Common/DbHelper.cs) 是所有业务实体读写的统一门面：内部维护每个实体类型的内存缓存（`DbTable`，按 `DbAccount` 分组），启动时把整表读进内存，之后的增删改查都先过内存缓存再落盘，删除的记录会保留 3 天供多端同步后才物理删除。新增实体类型需要在 `DbHelper` 静态构造函数的 `tableTypes` 列表里注册。

### 多账号支持

千牛支持多账号同时登录，所以核心类基本都是"一个账号一个实例"：`QN`（按 `Seller.Nick` 去重的 `QNSet`）、`Desk`（每个千牛接待窗口一个）。业务数据也都以 `DbAccount` 字段隔离。新增功能时要考虑多账号并存的场景，不要假设全局只有一个千牛账号在线。
