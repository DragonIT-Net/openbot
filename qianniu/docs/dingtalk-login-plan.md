# 钉钉登录集成 需求文档

> 本文档只做需求分析和设计，**不写代码**，等你确认后再进入编码阶段。这是一个独立文档（跟 [plan.md](../plan.md) 里的智能工单功能是两件事，互不影响）。

## 0. 需求汇总（我的理解，供你确认）

给这个 WPF 桌面客户端加一个**钉钉扫码登录**功能：

- 客户端启动后需要先登录（钉钉扫码认证），登录页面是新增的一个窗口。
- 登录信息需要存储，下次打开客户端不用每次都重新扫码。
- **单点登录**：同一个钉钉账号如果在另一台电脑上登录了，之前登录的那台会被强制下线（踢掉）。
- 应用内任何地方做操作时，都能拿到"当前登录人"的信息（昵称、账号标识等）。

## 1. 需要你在钉钉开放平台侧准备的东西（我这边做不了）

我去看了你给的[钉钉官方文档](https://open.dingtalk.com/document/development/tutorial-obtaining-user-personal-information)，确认了具体要求：

- 在[钉钉开放平台](https://open.dingtalk.com/)创建一个**企业内部应用**（不是第三方企业应用——原文写的是"因内部应用的安全考虑，企业内部应用不支持跨组织授权登录，即只有组织内的用户可以通过浏览器网页方式登录应用"，正好符合你们"只有自己公司客服能登录"这个场景；而且第三方企业应用拿到的手机号是脱敏的`155****3240`，企业内部应用才能拿到完整手机号）。
- 拿到应用的 `Client ID` 和 `Client Secret`（在应用详情页"凭证与基础信息"里）。
- 申请两个接口权限点：`Contact.User.mobile`（读手机号）和 `Contact.User.Read`（读通讯录个人信息），不申请的话拿不到对应字段。
- 在"开发配置 > 安全设置"里配置"重定向 URL（回调域名）"——**这个要填一个 `http://localhost:固定端口/回调路径`**，不是外网地址（原因见第 3 节，这跟我之前以为"需要公网回调地址"的设想不一样，实际上更简单，不需要公网服务器）。
- 发布应用（免管理员审批的话选"仅我可见"）。
- 这些都是钉钉后台的操作，不是代码层面的事，需要你去做。

## 2. 整体架构：AppSecret 不能放客户端里

`AppSecret` 是敏感凭证，桌面客户端（`.exe`）容易被反编译拿到，**不能把它嵌进 C# 代码或配置文件里**。跟智能工单那部分一样的思路——真正跟钉钉服务器交互的逻辑（换取 accessToken、拉取用户信息）放在你的 **Python 后端**，C# 客户端只做：

1. 找 Python 后端要一个登录二维码
2. 轮询这个二维码有没有被扫描确认
3. 确认后拿到"会话令牌"(sessionToken) + 用户信息，存到本地
4. 之后定期问 Python 后端"我的会话还有效吗"，没有效就说明被踢了，回到登录页

Python 后端是"真正持有 AppSecret、真正管理会话/踢人逻辑"的那一方；C# 客户端是"薄客户端"，只调接口，不碰敏感凭证。

## 3. 登录流程设计

### 3.1 扫码方式怎么实现（技术选型，看了官方文档后有修正）

官方文档里给了两种网页登录方式，我重新核实了一下哪种适合桌面客户端：

| 方案 | 说明 | 是否适合桌面客户端 |
|---|---|---|
| 方式二：内嵌二维码（JS SDK） | 钉钉提供一段 JS，嵌到你自己的网页里动态生成二维码 | **不适合**——文档明确要求"嵌入二维码的页面必须和 redirect_uri 同源"，这是个网页场景的限制，桌面客户端没有"网页同源"这个概念，除非我们自己再套一层网页服务，没必要这么绕 |
| 方式一：钉钉官方登录页（推荐） | 直接跳转到钉钉自己域名下的一个登录页面（`https://login.dingtalk.com/oauth2/auth?...`），二维码是钉钉自己渲染的，用户扫码确认后，钉钉把浏览器重定向到你配置好的 `redirect_uri`，带上一个 `code` 参数 | **适合**——桌面客户端标准做法：打开系统默认浏览器访问这个登录页，同时在本机启一个临时的本地 HTTP 监听（`http://localhost:固定端口/`）等着接这个重定向。这是很多桌面软件做"浏览器登录"的标准套路（比如各种 CLI 工具的 `login` 命令），.NET Framework 自带 `System.Net.HttpListener`，**不需要引入 WebView2/CefSharp 这类网页控件依赖** |

**推荐用方式一**：不用在项目里加新的网页控件依赖，用系统浏览器 + 本地回环监听就能实现。唯一的体验差异是会短暂跳出到浏览器窗口，而不是完全在 App 内嵌一个二维码——如果你更在意"完全不跳出 App"的体验，可以选择引入 WebView2 走方式二，但要多一个依赖、多处理"网页同源"这个限制，本期先按方式一来，除非你有其他考虑。

### 3.2 具体流程（已按官方文档修正）

1. 客户端启动，弹出登录窗口（`WndDingTalkLogin`），同时在本机启动一个临时的 `HttpListener`，监听 `http://localhost:{固定端口}/dingtalk/callback`（这个端口和路径需要**提前在钉钉开发者后台配置好**，作为"重定向URL"，见第 1 节）。
2. 客户端拼出钉钉登录地址并用系统默认浏览器打开：
   ```
   https://login.dingtalk.com/oauth2/auth?redirect_uri=http%3A%2F%2Flocalhost%3A{端口}%2Fdingtalk%2Fcallback&response_type=code&client_id={ClientId}&scope=openid&prompt=consent
   ```
3. 浏览器里显示的是**钉钉自己的登录页面**（钉钉自己渲染二维码），用户拿手机钉钉扫码确认。
4. 确认后，钉钉把浏览器重定向到 `http://localhost:{端口}/dingtalk/callback?code=xxx&authCode=xxx`（`code` 和 `authCode` 值一样，取一个就行）。
5. 客户端本机的 `HttpListener` 接住这个请求，拿到 `code`，给浏览器返回一个简单的"登录成功，可以关闭此页面"提示页，然后关闭监听。
6. 客户端把这个 `code` POST 给 Python 后端（见第 8 节接口契约），Python 后端拿着 `code` + 自己持有的 `ClientSecret`：
   - 调用钉钉「获取用户 token」接口换取用户 accessToken
   - 用这个 accessToken 调用「获取用户通讯录个人信息」接口（`unionId` 传 `"me"`，代表当前授权人自己），拿到 unionId、昵称、手机号
   - 生成一个 `sessionToken`，执行"踢掉该账号其他设备"的逻辑（见第 4 节）
   - 把 `sessionToken` + 用户信息一次性返回给 C# 客户端
7. C# 客户端拿到 `sessionToken` + 用户信息，登录窗口关闭，进入原有的主界面启动流程（[Bot/StartUp/BootStrap.cs](../src/Bot/StartUp/BootStrap.cs)）。
8. 客户端把 `sessionToken` + 用户信息缓存到本地（见第 5 节）。下次启动时，先尝试用本地缓存的 `sessionToken` 做一次"静默校验"，如果仍然有效，直接跳过登录进主界面；无效（过期/被踢）才弹登录窗口重新走一遍上面的流程。

**这一版流程比我最早设想的"生成二维码图片 + 轮询"要简单**：不需要"生成二维码"和"轮询状态"这两个接口了，因为本地 `HttpListener` 直接就能同步等到结果（第 6 步是一次性的，不是轮询），少了两个接口，第 8 节的接口契约也相应简化了。

## 4. 单点登录（踢人）机制

**服务端会话状态存 Redis**（这是 Python 后端内部的存储选型，不是 C# 客户端那边，客户端本地缓存仍然走 PersistentParams，见第 5 节）。Redis 天然适合这个场景：自带 TTL 过期、单账号覆盖写正好对应"踢掉旧设备"、每次心跳校验就是一次 key 查找，够快。

建议的 key 设计：

| Key | Value | TTL |
|---|---|---|
| `dingtalk:session:{unionId}` | 当前有效的 `sessionToken` | 跟登录会话有效期一致（比如 7 天） |
| `dingtalk:token:{sessionToken}` | 用户信息 JSON（unionId/nick/mobile） | 同上 |

流程：

- **新设备登录成功时**：
  1. 读 `dingtalk:session:{unionId}`，拿到旧的 `sessionToken`（如果有）
  2. 删掉 `dingtalk:token:{旧sessionToken}`（这一步就是"踢掉旧设备"——旧 token 直接查不到了）
  3. 覆盖写 `dingtalk:session:{unionId}` = 新 `sessionToken`，并写入 `dingtalk:token:{新sessionToken}` = 用户信息
- **校验会话时**（心跳/启动时静默校验）：直接查 `dingtalk:token:{sessionToken}` 存不存在，存在就是有效，不存在就是失效（过期或被踢，两种情况客户端都当"需要重新登录"处理，不用细分）。

**旧客户端怎么知道自己被踢了**：登录成功后客户端启动一个心跳定时器（比如每 30 秒），调用「校验会话」接口带上自己的 `sessionToken`。服务端在 Redis 里查不到时返回"失效"，客户端弹提示"您的账号在其他设备登录，已被强制下线"，然后退回登录页面（清空本地缓存的 session）。

这是"客户端轮询发现"的方式，实现简单但有最长 30 秒的感知延迟。如果需要"服务端主动推送、立即踢人"，可以考虑复用项目里已有的 `MyWebSocketServer`（[Bot/ChromeNs/MyWebSocketServer.cs](../src/Bot/ChromeNs/MyWebSocketServer.cs)）思路另起一条长连接，或者跟 Python 之间搞 WebSocket/SSE，甚至用 Redis 自带的发布订阅（pub/sub）——**这个本期不做，先用轮询，作为后续优化项**（见第 9 节待确认）。

## 5. 本地会话存储（客户端这边，跟第 4 节的 Redis 是两回事）

第 4 节的 Redis 是**服务端**用来判断"谁当前登录有效、要不要踢人"的；这一节是**客户端本地**缓存一份 `sessionToken`，纯粹是为了"下次打开客户端不用重新扫码"，两者不冲突，客户端这边不需要也不应该直接连 Redis。

不新建数据库表，复用项目里已有的 `PersistentParams` 机制（`BotLib/Db/Sqlite/PersistentParams.cs`），跟现有 AI 设置（`Params.Robot.GetApiKey()` 那一套）是同样的模式，存：

- `DingTalkUnionId`
- `DingTalkNick`（昵称）
- `DingTalkMobile`（手机号，如果钉钉授权范围里能拿到）
- `SessionToken`
- `LoginTime`

## 6. 全局"当前登录人"访问

新增一个静态类 `Bot/DingTalk/CurrentDingTalkUser.cs`，模式类似项目里已有的 `QN.CurQN`（当前活跃的千牛账号）：

```csharp
public static class CurrentDingTalkUser
{
    public static string UnionId { get; set; }
    public static string Nick { get; set; }
    public static string Mobile { get; set; }
    public static string SessionToken { get; set; }
    public static bool IsLoggedIn => !string.IsNullOrEmpty(SessionToken);
}
```

应用启动完成登录后填充这几个字段，之后任何地方（比如以后要在工单里记录"是哪个客服提交的"）都可以直接读 `CurrentDingTalkUser.Nick` 之类的属性，不用每次都重新查。

## 7. 登录页面 UI

- 新增 `Bot/Common/Windows/WndDingTalkLogin.xaml` + `.xaml.cs`（跟目前 `Common/Windows/` 下其它弹窗一样的风格/位置）。
- 界面元素：
  - 提示文案（"请在弹出的浏览器窗口中完成钉钉扫码登录"）
  - 加载中状态（等待浏览器完成登录 / 正在跟服务器交换信息 / 登录失败可重试 几种状态的文案切换）
  - "重新打开浏览器"按钮（万一用户不小心把浏览器窗口关了，或者想换个钉钉账号重新扫）
- **启动流程接入点**：[Bot/StartUp/BootStrap.cs](../src/Bot/StartUp/BootStrap.cs) 的 `Init()` 方法，在 `DeskScanner.LoopScan()`/`MyWebSocketServer.WSocketSvrInst.Start()` 等现有初始化逻辑**之前**，先做登录校验/弹登录窗口，登录通过才继续走后面的初始化。

## 8. 接口契约（C# ↔ Python，草案，具体路径你可以按你后端习惯调整）

只需要 2 个接口（比最早设想的少 1 个，因为不再需要"生成二维码"和"轮询状态"这两步，本地 `HttpListener` 直接同步拿到 `code`）。

### 8.1 用 code 换取会话

```
POST {baseUrl}/api/dingtalk/login/exchange
```

请求：
```json
{ "code": "f85c6*****7b77" }
```

响应：
```json
{
  "sessionToken": "xxxx",
  "unionId": "...",
  "nick": "张三",
  "mobile": "138****0000"
}
```

失败（比如 code 过期/无效）时返回非 200，C# 端提示"登录失败，请重试"，回到第 7 步的登录窗口让用户重新点一次登录。

### 8.2 校验会话（登录成功后的心跳 + 启动时的静默校验共用）

```
POST {baseUrl}/api/dingtalk/session/validate
```

请求：
```json
{ "sessionToken": "xxxx" }
```

响应：
```json
{ "valid": true }
```
或
```json
{ "valid": false, "reason": "kicked_by_other_device" }
```

## 9. 关于 Redis：有没有必要加？

老实说：**不是必须的，但如果你后端本来就有 Redis，用它是最省事的**。这个"单账号只认一个有效 session、新登录踢掉旧的"的需求，本质上就是"存一个 key，能覆盖写，能过期"，Redis 确实是这类需求的标准解法（第 4 节的设计），但不是唯一解法：

- 如果 Python 后端已经在用的数据库（不管是 MySQL/PostgreSQL/SQLite）加一张 `sessions` 表（字段：`union_id`、`session_token`、`expires_at`），一样能实现——校验就是查 `WHERE session_token = ? AND expires_at > NOW()`，踢人就是登录时 `DELETE FROM sessions WHERE union_id = ?` 再插入新的一行。这条查询在这种量级（客服人数不会很多，心跳请求量很小）下性能完全够用，不需要专门为了这个功能去新增一个 Redis 部署。
- 如果你们后端**已经在用 Redis** 做别的事情（缓存、任务队列之类的），那顺手用它存这个 session 完全没问题，直接按第 4 节的设计来。
- 如果**目前没有 Redis**，我建议不要单独为了这一个小功能去新增部署一套 Redis——多一个组件就多一分运维负担（部署、监控、内存丢失后的会话失效等问题），用现有数据库的一张表更简单。

**这个由你决定**：告诉我你们 Python 后端现在有没有 Redis，我把第 4 节的设计相应改成"Redis 版"或"数据库表版"（两版的 C# 客户端调用方式完全一样，`POST /api/dingtalk/session/validate` 这个接口不受影响，区别只在 Python 内部怎么存）。

## 10. 待你确认的问题

1. ~~钉钉应用类型~~：已确认用**企业内部应用**（见第 1 节，符合"只有本公司客服能登录"的场景，且能拿到完整手机号）。
2. ~~Python 后端的公网回调地址~~：**不需要了**——用了方式一（系统浏览器 + 本地回环监听）之后，钉钉重定向的目标是客户端本机的 `localhost`，不是 Python 后端，Python 后端不需要暴露公网回调地址。但要确认一下：**固定用哪个本地端口**（比如 `17890`），需要先在钉钉开发者后台把这个端口配置成"重定向URL"，客户端和钉钉后台配置要对得上。
3. **Redis 还是数据库表**：见第 9 节，需要你告诉我现状。
4. **踢人感知方式**：先用轮询（本文档默认方案，30 秒一次心跳），还是要在本期就做成服务端主动推送？
5. **未登录状态下客户端能不能用**：是完全阻塞在登录页面（不登录啥也干不了），还是允许有限功能可用（比如接待窗口自动化照常跑，只是某些跟"当前登录人"绑定的功能不可用）？我倾向前者（阻塞式登录），简单清晰，除非你有别的考虑。
6. 手机号这类信息授权范围钉钉那边需要额外申请权限（第 1 节提到的 `Contact.User.mobile`），确认一下你申请的应用权限范围里有没有这个，没有的话 `CurrentDingTalkUser.Mobile` 就一直是空。

## 11. 涉及文件清单（预估，写代码前再细化确认）

**新增：**
- `Bot/DingTalk/CurrentDingTalkUser.cs`
- `Bot/DingTalk/IDingTalkAuthClient.cs` + 实现类（封装上面 2 个接口的 HTTP 调用）
- `Bot/DingTalk/DingTalkLoopbackListener.cs`（封装 `System.Net.HttpListener`，打开浏览器 + 本地监听回调，拿到 `code` 就返回）
- `Bot/DingTalk/DingTalkSession.cs`（本地会话读写，封装 `PersistentParams` 存取）
- `Bot/Common/Windows/WndDingTalkLogin.xaml` + `.xaml.cs`

**修改：**
- `Bot/StartUp/BootStrap.cs`：接入登录校验流程
