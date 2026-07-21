# 智能工单功能开发计划

## 0. 需求汇总（我的理解，供你确认）

在客服工作台（`CtlRobot`，也就是 [RightPanel.xaml.cs](src/Bot/AssistWindow/Widget/RightPanel.xaml.cs) 里当前唯一的"工作台" tab 内容）的头部，新增一排 3 个图标 Tab：**聊天记录 / 智能工单 / 智能问答**，用来切换下面的内容区：

- **聊天记录**：就是现在已有的对话气泡列表（[CtlRobot.xaml](src/Bot/AssistWindow/Widget/Robot/CtlRobot.xaml) 里的 `scvBody`/`stkDialog`），原样保留。
- **智能工单**：一个问题类型下拉框（订单类问题/发票类问题/技术类问题/客诉类问题）+ "确认"按钮。点击确认后，弹出一个表单窗口，展示**从当前买家聊天记录里提取出的表单字段**（本期先做到"弹窗展示可编辑表单"这一步，不做提交保存）。
- **智能问答**：本期留空占位，先把 tab 框架搭出来。

同时需要设计：
- 聊天记录持久化：**改为消息一来就实时推给 Python 后端存储**（Python 那边有持久化数据库，不怕 C# 本地重装/换机器丢数据），C# 本地不再单独存一份聊天记录数据库。
- 根据"当前客服 + 买家昵称"从聊天记录里提取表单字段的接口契约（实现你会另外写，我只定义接口和数据结构，本期用一个返回空数据的桩实现，让 UI 能跑通）。
- 4 类问题里，店铺/机型/远程软件/是否可评价 是下拉单选，选项从一个接口一次性读取（你来实现这个接口，我只定义契约和桩实现）。

## 1. 本期范围边界（明确不做的部分）

- ❌ 智能问答 tab 的实际内容——先留空。
- ❌ 把聊天消息实时推给 Python 的真实网络调用——你说了另外写/确认细节，我只给推送接口的契约和挂载位置建议（本期先用一个空实现占位，不实际发请求）。
- ❌ 从聊天记录提取表单字段的算法/接口实现——同上，我只定义接口契约，本期用桩实现（返回空表单，字段可手动填）。
- ❌ 弹窗表单的"提交/保存"持久化——本期只做"展示 + 可编辑"，不做保存动作（弹窗放"确认"按钮，但点击目前只关闭弹窗）。
- ❌ 下拉选项（店铺/机型/远程软件/是否可评价）的真实接口实现——你来写，我只定义接口契约，本期用桩实现返回几条默认数据占位。

## 2. UI 设计

### 2.1 三个 Tab 的位置和切换

[CtlRobot.xaml](src/Bot/AssistWindow/Widget/Robot/CtlRobot.xaml) 现有结构是 Row1 买家信息条 / Row2 内容区（`scvBody`）/ Row3 "模型问答"占位框（"预留流式问答区域"）。本次改动：

- **删除 Row3**（那个"模型问答"占位框整个移除，Grid 从 4 行收缩成 3 行：`Auto` 商品占位行 / `40` 买家信息条 / `*` 内容区）。这块"模型问答/流式问答"的能力**移到第三个 Tab（智能问答）里**，作为它的占位内容，等你后续实现。
- **Row1 和 Row2 之间插入一行图标 Tab 条**，切换 Row2 显示的内容：

| Tab | 图标 | 内容 |
|---|---|---|
| 聊天记录（默认选中） | 聊天气泡图标 | 现有的 `scvBody`（原样不动，不改动任何一行现有代码） |
| 智能工单 | 工单/清单图标 | 新增的 `CtlTicket` 控件 |
| 智能问答 | 问答气泡图标 | 占位内容：把原来 Row3 那段"模型问答 / 预留流式问答区域"的文案搬过来当占位显示，标记这里未来是流式问答功能的位置 |

智能工单 tab 内容区不需要再单独隐藏"模型问答"框了（因为它已经不在 `CtlRobot` 主体里了）。

图标方案：项目目前没有现成的"聊天/工单/问答"图标素材，用**内联矢量 `Path`/`Geometry`** 画三个简单线性小图标（不新增图片资源文件，方便后续统一换色）。

### 2.2 智能工单 Tab 内容（新增 `CtlTicket` 控件）

- `ComboBox`：问题类型，选项为 订单类问题/发票类问题/技术类问题/客诉类问题，默认选中"订单类问题"。
- `Button`："确认"。
- 点击确认后：
  1. 从 `QN.CurQN` 拿当前 `Seller.Nick` / `Buyer.Nick`（跟 `CtlRobot` 现有的 `_preQN`/`txtBuyer` 取值逻辑一致）。
  2. 调用 `ITicketFormExtractor.ExtractAsync(sellerNick, buyerNick, category)`，拿到 `TicketFormData`（本期是桩实现，返回一个只填了 Seller/Buyer/Category、其余字段为空的对象，字段允许手动填）。
  3. 打开 `WndTicketForm` 弹窗，把 `TicketFormData` 传进去展示。

### 2.3 弹窗表单（新增 `WndTicketForm` 窗口）

**一个窗口，字段按类别动态显示/隐藏**，不做 4 套独立窗口（4 类问题字段大量重叠，动态显示更省事也更好维护）。

字段清单：

| 字段 | 订单类 | 客诉类 | 发票类 | 技术类 | 控件类型 |
|---|---|---|---|---|---|
| 客户身份标识（买家昵称/订单号/电话，见 3.1 的 `CustomerIdentity`） | ✓ | ✓ | ✓ | ✓ | 文本框 |
| 店铺 | ✓ | ✓ | ✓ | ✓ | 下拉单选 |
| 问题描述/用户需求 | ✓ | ✓ | ✓ | ✓ | 多行文本框 |
| 附件 | ✓ | ✓ | ✓ | ✓ | 文本框（路径）+ "选择文件"按钮 |
| 邮箱 | | | ✓ | | 文本框 |
| 是否引导后台自助开票 | | | ✓ | | 下拉（是/否） |
| 机型 | | | | ✓ | 下拉单选 |
| 电话/微信号/远程号 | | | | ✓ | 文本框 |
| 远程软件 | | | | ✓ | 下拉单选 |
| 是否可评价 | | | | ✓ | 下拉（是/否） |

弹窗放**"确认"按钮**。本期后端还没有真正的持久化接口，所以点击"确认"目前只是关闭弹窗（不做实际保存动作），等你把工单提交的持久化接口写好之后，再把这个按钮接上真正的保存逻辑。

## 3. 数据设计

### 3.1 工单相关 DTO/枚举（新增，业务域文件夹 `Bot/Ticket/`）

- `Bot/Ticket/TicketCategoryEnum.cs`：`OrderIssue`（订单类问题）、`InvoiceIssue`（发票类问题）、`TechIssue`（技术类问题）、`ComplaintIssue`（客诉类问题）。
- `Bot/Ticket/TicketFormData.cs`：包含 Seller/Buyer/Category 三个上下文字段 + 上表里全部字段（`CustomerIdentity`、`Shop`、`Description`、`AttachmentPath`、`Email`、`GuideSelfInvoice`、`DeviceModel`、`ContactChannel`、`RemoteSoftware`、`CanRate`）。**`CustomerIdentity` 对应 Python 接口返回的 `customerIdentity` 字段**（取值优先级：买家昵称 > 订单号 > 电话，千牛平台下大部分情况就是买家昵称），命名上不叫 `Phone` 了。不用的字段留空即可，不用为每个类别单独建类。
- `Bot/Ticket/TicketFieldDefinitions.cs`：静态方法 `GetVisibleFields(TicketCategoryEnum category)`，返回该类别要显示的字段列表，供 `WndTicketForm` 动态显示/隐藏用。上表就是这个方法的数据来源。
- `Bot/Ticket/ITicketFormExtractor.cs`：接口，`Task<TicketFormData> ExtractAsync(string sellerNick, string buyerNick, TicketCategoryEnum category)`。
- `Bot/Ticket/TicketFormExtractor.cs`：本期的桩实现，只填 Seller/Buyer/Category，其余留空。**后续你写真正的提取逻辑时，直接替换/重写这个类的方法体，接口签名不用变，UI 侧不用改。**

### 3.2 聊天记录实时推送（新增 `Bot/ChatRecord/IChatMessagePublisher.cs`）

**不落 C# 本地数据库**，改成消息一来就实时推给 Python 后端（`POST {baseUrl}/ticket/chat/messages`），由 Python 那边的数据库持久化（更耐用，不怕本地这台电脑重装/换机器）。**这个接口的契约已经由 Python 那边定稿**，契约：

```csharp
public interface IChatMessagePublisher
{
    Task PublishAsync(string sellerNick, string shopName, List<ChatMessageDto> messages);
}

public class ChatMessageDto
{
    public string Ccode { get; set; }
    public string BuyerNick { get; set; }
    public string FromNick { get; set; }
    public string ToNick { get; set; }
    public bool IsBuyerSend { get; set; }
    public DateTime SendTime { get; set; }
    public int TemplateId { get; set; }
    public string MessageText { get; set; }
    public string FileId { get; set; }
    public string FileUrl { get; set; }
    public string ClientId { get; set; }
    public string MessageId { get; set; }
    public string OrderNo { get; set; }   // 新增：能明确关联到订单号时传，拿不到留空，不要瞎填
}
```

`shopName` 是纯店铺名（跟 `sellerNick` 是两个概念，`sellerNick` 通常是"店铺名:客服名"的组合）。项目里现有的 `Seller`（`LocalUser`）对象没有现成的"纯店铺名"字段，**需要从 `Seller.Nick`/`Display` 按 `:` 分隔取前半段解析**，具体解析方式写代码时再确认一下是否所有账号的昵称格式都是这个规律。

本期的 `ChatMessagePublisher`（空实现占位）方法体先什么都不做（或者只打个日志），**不实际发网络请求**，等 baseUrl/ApiKey 配置好后再把方法体换成真正的 HTTP 调用。

**建议的调用挂载点**：[Bot/ChromeNs/QN.cs](src/Bot/ChromeNs/QN.cs) 的 `Cdp_EvRecieveNewMessage` 方法里，`chatRes.result` 循环外（拿到整批 `messages` 之后，一次性调用 `PublishAsync`，不用每条消息单独发一次请求）。现在这段代码只处理"买家发给客服"方向的消息（用于触发 AI 自动回复），推送时**不做方向过滤**，买家发的和客服发的都推。具体的 HTTP 接口格式、字段映射、鉴权 Header，见 [docs/ticket-extraction-interface.md](docs/ticket-extraction-interface.md)。

### 3.3 下拉选项接口（新增 `Bot/Ticket/ITicketOptionProvider.cs`）

"店铺/机型/远程软件/是否可评价"这几个下拉字段的候选值**不落本地数据库**，改成你写一个接口一次性返回全部选项。契约：

```csharp
public class TicketOptionSet
{
    public List<string> Shops { get; set; }
    public List<string> DeviceModels { get; set; }
    public List<string> RemoteSoftwares { get; set; }
    public List<string> CanRateOptions { get; set; }   // 一般就是 ["是","否"]
}

public interface ITicketOptionProvider
{
    Task<TicketOptionSet> GetOptionsAsync();
}
```

本期的 `TicketOptionProvider`（桩实现）返回几条写死的默认数据占位（`CanRateOptions` 直接给 `["是","否"]`），让下拉框和 UI 能跑起来。**你后面接自己的接口时，把这个类的方法体换成真正的 HTTP 调用即可，接口签名不用变，UI 侧不用改。**候选值维护在 Python 侧的数据字典管理页面，这个接口本身契约不变（`GET {baseUrl}/ticket/options`）。具体实现指引见 [docs/ticket-extraction-interface.md](docs/ticket-extraction-interface.md)。

### 3.4 接口鉴权

Python 那 3 个接口（推送消息/提取表单/下拉选项）**都需要带鉴权 Header**：`X-Api-Key: <约定好的 Key>`。这个 Key 跟 `baseUrl` 一样，走设置界面配置 + `PersistentParams` 存储，不要写死在代码里（参照现有 `Params.Robot.GetApiKey()` 的模式）。三个接口对应的 C# 实现类（`ChatMessagePublisher`/`TicketFormExtractor`/`TicketOptionProvider`）在真实发请求时都要带上这个 Header。

### 3.5 DbHelper

本轮不需要新增 DbEntity/不需要动 `DbHelper.cs`——聊天记录不落本地库，改成实时推送给 Python（见 3.2）。

## 4. 涉及文件清单

**新增：**
- `Bot/Ticket/TicketCategoryEnum.cs`
- `Bot/Ticket/TicketFormData.cs`
- `Bot/Ticket/TicketFieldDefinitions.cs`
- `Bot/Ticket/ITicketFormExtractor.cs`
- `Bot/Ticket/TicketFormExtractor.cs`（桩实现）
- `Bot/Ticket/ITicketOptionProvider.cs` + `TicketOptionSet.cs`
- `Bot/Ticket/TicketOptionProvider.cs`（桩实现）
- `Bot/ChatRecord/IChatMessagePublisher.cs` + `ChatMessageDto.cs`
- `Bot/ChatRecord/ChatMessagePublisher.cs`（空实现占位）
- `Bot/AssistWindow/Widget/Ticket/CtlTicket.xaml` + `.xaml.cs`（智能工单 tab 内容）
- `Bot/Common/Windows/WndTicketForm.xaml` + `.xaml.cs`（弹窗表单）

**修改：**
- `Bot/AssistWindow/Widget/Robot/CtlRobot.xaml` + `.xaml.cs`：加图标 Tab 条 + 内容切换逻辑
- `Bot/ChromeNs/QN.cs`：`Cdp_EvRecieveNewMessage` 里调用 `IChatMessagePublisher.PublishAsync`（本期是空实现，不实际发请求）

## 5. 确认结果

1. 弹窗放"确认"按钮（点击目前只关闭弹窗，不做实际保存，见 2.3）。
2. 附件字段本期只做"文本框显示路径 + 选择文件按钮"，不做实际文件复制/校验。
3. 图标本期用简单矢量占位图标。
4. 聊天记录改为**实时推送给 Python 持久化**（不落 C# 本地库），推送和提取接口的真实实现由你来写；我保证 `IChatMessagePublisher`/`ITicketFormExtractor` 接口签名稳定。
5. **Python 接口契约已定稿**（你直接给的规格）：3 个接口路径都在 `{baseUrl}/ticket/...` 下，统一带 `X-Api-Key` 鉴权 Header；`TicketFormData` 里原来的 `Phone` 字段改名成 `CustomerIdentity`（对应 Python 返回的 `customerIdentity`）；推送消息新增 `shopName`（整批级别）和 `orderNo`（单条消息级别）两个字段。对应的实现指引见 [docs/ticket-extraction-interface.md](docs/ticket-extraction-interface.md)。
