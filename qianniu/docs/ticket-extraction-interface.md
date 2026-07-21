# 智能工单：与 Python 接口服务对接指引

> 本文档的接口契约已经由 Python 那边定稿（不是我这边的草案了），C# 侧照这个来实现即可。

你的表单提取 + 下拉选项 + 聊天记录存储服务是用 Python 写的，跟 C# 客户端之间走 **HTTP REST**。整体思路：

- **聊天记录不落 C# 本地数据库**。消息一来，C# 就实时把它推给 Python，由 Python 后端的数据库持久化（更耐用，不怕这台电脑重装/换机器丢数据）。
- 提取表单的时候，C# 只需要告诉 Python"帮我查这个客服+买家的表单"，**不需要再把聊天记录打包发过去**——因为 Python 自己已经存了一份完整的，直接查自己的库就行（提取时只看"最近一次会话"，按 `sendTime` 算，相邻消息间隔超过阈值就不再往前追溯，不会把很久以前的旧工单聊天记录也拿来一起提取——这是 Python 侧的实现细节，C# 不用管）。

本文档定义了 C# 客户端要调用的 **3 个 HTTP 接口**的契约，以及 C# 侧对应的桩实现怎么改成真正的调用。

UI 侧（Tab 切换、下拉框、弹窗展示）不关心底层是本地实现还是远程 HTTP 调用，只认 `IChatMessagePublisher`/`ITicketFormExtractor`/`ITicketOptionProvider` 这三个 C# 接口。**只要接口签名不变，内部换成 HTTP 调用完全不影响 UI 代码。**

> **已知的临时方案，后续要改**：现在"推送聊天消息"（第 2 节）是 C# 直接同步 HTTP POST 给 Python，网络失败就丢（见 2 节"边界情况"）。这只是当前阶段图省事的做法，**后续要改成走 MQ（消息队列）**——C# 把消息丢进队列就算完事，Python 从队列里消费，这样即使 Python 服务短暂挂了/重启，消息也不会丢，还能有重试、削峰的效果。等 MQ 方案定下来，`IChatMessagePublisher` 的实现类换个内部逻辑（从"HTTP POST"换成"发消息到队列"）就行，接口签名不用变，UI 侧不用动。`ITicketFormExtractor`/`ITicketOptionProvider` 这两个是一问一答式的调用，不适合走 MQ，还是保持 HTTP。

---

## 1. 三个接口一览 + 鉴权

| 接口 | 方向 | 用途 |
|---|---|---|
| `POST {baseUrl}/ticket/chat/messages` | C# → Python | 消息一来就实时推送，Python 侧持久化 |
| `POST {baseUrl}/ticket/extract` | C# → Python | 根据客服+买家昵称，查 Python 自己存的聊天记录，用大模型结构化提取表单字段 |
| `GET {baseUrl}/ticket/options` | C# → Python | 一次性拿到店铺/机型/远程软件/是否可评价 的下拉候选值 |

**Base URL** 和 **API Key** 都做成可配置项（跟现有 AI 设置 `Params.Robot.GetBaseUrl()`/`GetApiKey()` 一样的模式，走 `PersistentParams`，在设置界面里填），不要写死在代码里。

**所有接口请求头都要带鉴权**：
```
X-Api-Key: <约定好的 Key>
```

请求体、响应体统一 `application/json`。

---

## 2. 接口一：推送聊天消息

```
POST {baseUrl}/ticket/chat/messages
```

消息一来就实时推送，Python 侧持久化。买家发的和客服发的都要推（不做方向过滤），按批次一次性推送，不要每条单独发请求。

### 挂载点

[Bot/ChromeNs/QN.cs](../src/Bot/ChromeNs/QN.cs) 的 `Cdp_EvRecieveNewMessage` 方法。现在这段代码只处理"买家发给客服"方向的消息（用于触发 AI 自动回复）。**建议新增一段独立的推送逻辑，不要和现有 AI 回复分支混在一起**，拿到整批 `chatRes.result` 之后一次性调用 `PublishAsync`。

### 请求体

```json
{
  "sellerNick": "精臣数码专营店:傲傲",
  "shopName": "精臣数码专营店",
  "messages": [
    {
      "ccode": "2466875817.1-2200548359188.1#11001@cntaobao",
      "buyerNick": "longlonglong17619853",
      "fromNick": "longlonglong17619853",
      "toNick": "精臣数码专营店:傲傲",
      "isBuyerSend": true,
      "sendTime": "2026-07-20T09:16:07",
      "templateId": 102,
      "text": "我的订单号是1234567890，一直没发货",
      "fileId": "",
      "fileUrl": "",
      "clientId": "7483792878414594105",
      "messageId": "4215923574453.PNM",
      "orderNo": "1234567890"
    }
  ]
}
```

### 字段说明

| 字段 | 必填 | 说明 |
|---|---|---|
| `sellerNick` | 是 | 客服账号昵称（跟 `fromNick`/`toNick` 里的客服那一侧一致） |
| `shopName` | 是 | 纯店铺名称，跟 `sellerNick` 是两个概念（`sellerNick` 通常是"店铺名:客服名"的组合）。**取值来源**：项目里现有的 `Seller`（`LocalUser`）对象没有现成的"纯店铺名"字段，需要从 `Seller.Nick`/`Display` 按 `:` 分隔取前半段解析，写代码时确认一下所有账号的昵称格式是否都符合这个规律 |
| `messages[].ccode` | 是 | 会话编码，取 `m.cid.ccode` |
| `messages[].buyerNick` | 是 | 买家昵称，根据 `m.IsBuyerSend` 取 `m.fromid.nick`/`m.toid.nick` 里不是 seller 的那个 |
| `messages[].fromNick` / `toNick` | 是 | `m.fromid.nick` / `m.toid.nick` |
| `messages[].isBuyerSend` | 是 | `m.IsBuyerSend`（已有计算属性：`loginid.nick == toid.nick`） |
| `messages[].sendTime` | 是 | ISO 格式时间字符串，如 `2026-07-20T09:16:07`，取 `long.Parse(m.sendTime).xTimeStampToDate()` 后格式化。**提取表单时会按这个字段做会话切分，务必保证准确** |
| `messages[].templateId` | 否 | `m.templateId` |
| `messages[].text` | 否 | `m.MessageText`（已有计算属性，取 `originalData.text` + `header.summary`） |
| `messages[].fileId` / `fileUrl` | 否 | `m.originalData?.fileId` / `m.originalData?.url`，`m.HasImage` 为 true 时才有意义 |
| `messages[].clientId` / `messageId` | 是 | `m.mcode.clientId` / `m.mcode.messageId`，配合 `ccode` 做去重键，重复推送会被 Python 侧自动跳过 |
| `messages[].orderNo` | 否 | 如果这条消息能明确关联到某个订单号（消息模板本身携带订单信息、或聊天上下文能拿到），传这个订单号；拿不到就传空字符串，**不要瞎填**。C# 端目前没有现成的"从消息里解析订单号"的逻辑，暂时留空即可，后续要不要做订单号识别再讨论 |

### 响应体

```json
{ "success": true }
```

### 边界情况

- 网络失败/超时：C# 端**不阻塞消息处理主流程**，fire-and-forget（不等待结果，或设置较短超时后直接放弃），失败只记日志，不重试。
- 去重键是 `ccode + clientId + messageId`，同一条消息重复推送 Python 侧会自动跳过，不会重复入库，C# 端不用自己做去重判断。

---

## 3. 接口二：提取工单表单

```
POST {baseUrl}/ticket/extract
```

根据客服+买家昵称，查 Python 自己存的聊天记录，用大模型结构化提取表单字段。**不需要带聊天记录**，Python 自己查库。

### 请求体

```json
{
  "sellerNick": "精臣数码专营店:傲傲",
  "buyerNick": "longlonglong17619853",
  "category": "OrderIssue"
}
```

`category` 固定四选一（英文枚举名）：`OrderIssue` / `InvoiceIssue` / `TechIssue` / `ComplaintIssue`。

### 响应体

```json
{
  "customerIdentity": "longlonglong17619853",
  "shop": "精臣数码专营店",
  "description": "订单一直没发货",
  "attachmentPath": "",
  "email": "",
  "guideSelfInvoice": "",
  "deviceModel": "",
  "contactChannel": "",
  "remoteSoftware": "",
  "canRate": ""
}
```

> **注意**：这个字段是 `customerIdentity`，不是最早讨论里的 `phone`，对应 C# 端 `TicketFormData.CustomerIdentity`，解析字段名要对上。

### 字段说明

| 字段 | 说明 |
|---|---|
| `customerIdentity` | 客户身份标识，取值优先级：买家昵称 > 订单号 > 聊天记录里的电话号码（千牛平台买家昵称基本总有值，所以这个字段大部分情况下就是买家昵称；其它平台昵称拿不到时才会退到订单号/电话） |
| `shop` | 店铺名称，直接取推送消息时传的 `shopName`，不是大模型猜的 |
| `description` | 问题描述/诉求总结 |
| `attachmentPath` | 附件说明（文字描述） |
| `email` | 邮箱，仅 `InvoiceIssue` 类别会填 |
| `guideSelfInvoice` | 是否已引导自助开票（`"是"`/`"否"`），仅 `InvoiceIssue` 类别会填 |
| `deviceModel` / `contactChannel` / `remoteSoftware` / `canRate` | 仅 `TechIssue` 类别会填 |

不属于当前 `category` 的字段会返回空字符串，C# 端只需要显示 `TicketFieldDefinitions.GetVisibleFields(category)` 对应的那几个。

### 每个类别对应哪些字段（供参照，跟 `TicketFieldDefinitions` 一致）

| 字段 | 订单类 | 客诉类 | 发票类 | 技术类 |
|---|---|---|---|---|
| customerIdentity | ✓ | ✓ | ✓ | ✓ |
| shop | ✓ | ✓ | ✓ | ✓ |
| description | ✓ | ✓ | ✓ | ✓ |
| attachmentPath | ✓ | ✓ | ✓ | ✓ |
| email | | | ✓ | |
| guideSelfInvoice | | | ✓ | |
| deviceModel | | | | ✓ |
| contactChannel | | | | ✓ |
| remoteSoftware | | | | ✓ |
| canRate | | | | ✓ |

### 边界情况

- 查不到聊天记录（还没推送过 / 新买家）：返回全部字段为空字符串的表单，不会返回 4xx/5xx。
- 识别到的下拉字段值（`shop`/`deviceModel`/`remoteSoftware`/`canRate`）不在 `options` 接口返回的候选列表里：Python 侧照常返回识别到的文本，C# 端下拉框选不中时显示为空，客服自己选，不需要 Python 做校验。
- 请求超时或非 200：C# 端捕获异常，返回只填了 `SellerNick`/`BuyerNick`/`Category` 的空表单，不阻塞 UI。

---

## 4. 接口三：下拉选项列表（未变动）

```
GET {baseUrl}/ticket/options
```

### 响应体

```json
{
  "shops": ["精臣数码专营店", "..."],
  "deviceModels": ["B21", "B3S", "..."],
  "remoteSoftwares": ["向日葵", "ToDesk", "..."],
  "canRateOptions": ["是", "否"]
}
```

候选值维护在 Python 侧的数据字典管理页面，C# 端不用管怎么维护的。

### 边界情况

- 请求失败：C# 端返回每个 List 都是空列表（不是 null）的 `TicketOptionSet`，`ComboBox.ItemsSource` 绑定空列表不会报错，下拉框显示为空，不影响其他字段正常填写。

---

## 5. C# 客户端实现（三个接口对应的真实实现）

接口签名（`CustomerIdentity` 已改名，`PublishAsync` 加了 `shopName` 参数，`ChatMessageDto` 加了 `OrderNo`）：

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
    public string OrderNo { get; set; }
}

public interface ITicketFormExtractor
{
    Task<TicketFormData> ExtractAsync(string sellerNick, string buyerNick, TicketCategoryEnum category);
}

public class TicketFormData
{
    public string SellerNick { get; set; }
    public string BuyerNick { get; set; }
    public TicketCategoryEnum Category { get; set; }

    public string CustomerIdentity { get; set; }   // 原 Phone，对应 JSON customerIdentity
    public string Shop { get; set; }
    public string Description { get; set; }
    public string AttachmentPath { get; set; }
    public string Email { get; set; }
    public string GuideSelfInvoice { get; set; }
    public string DeviceModel { get; set; }
    public string ContactChannel { get; set; }
    public string RemoteSoftware { get; set; }
    public string CanRate { get; set; }
}

public interface ITicketOptionProvider
{
    Task<TicketOptionSet> GetOptionsAsync();
}
```

真实实现思路（用 `System.Net.Http.HttpClient`，项目已经在用 `System.Net.Http`；每个请求都要带 `X-Api-Key` Header）：

```csharp
public class ChatMessagePublisher : IChatMessagePublisher
{
    public async Task PublishAsync(string sellerNick, string shopName, List<ChatMessageDto> messages)
    {
        if (messages == null || messages.Count < 1) return;
        try
        {
            var payload = new { sellerNick, shopName, messages };
            var baseUrl = Params.Ticket.GetApiBaseUrl();
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", Params.Ticket.GetApiKey());
                await client.PostAsync(
                    baseUrl.TrimEnd('/') + "/ticket/chat/messages",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex); // 推送失败不影响主流程，只记日志，不重试
        }
    }
}

public class TicketFormExtractor : ITicketFormExtractor
{
    public async Task<TicketFormData> ExtractAsync(string sellerNick, string buyerNick, TicketCategoryEnum category)
    {
        var fallback = new TicketFormData { SellerNick = sellerNick, BuyerNick = buyerNick, Category = category };
        try
        {
            var payload = new { sellerNick, buyerNick, category = category.ToString() };
            var baseUrl = Params.Ticket.GetApiBaseUrl();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", Params.Ticket.GetApiKey());
                var response = await client.PostAsync(
                    baseUrl.TrimEnd('/') + "/ticket/extract",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<TicketFormData>(json);
                result.SellerNick = sellerNick;
                result.BuyerNick = buyerNick;
                result.Category = category;
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return fallback;
        }
    }
}
```

`TicketOptionProvider` 同理，换成 `GET {baseUrl}/ticket/options`（带 `X-Api-Key` Header），失败时返回一个每个 List 都是空列表的 `TicketOptionSet`。

**还需要你确认/补充的点：**
1. `X-Api-Key` 具体的 Key 值，以及设置界面里要不要新增一个输入框给这个 Key（还是先写死在配置文件里手动改）。
2. `orderNo` 这个字段 C# 端目前没有现成的解析逻辑（消息模板/聊天上下文里怎么识别订单号），本期先固定传空字符串，要不要做识别后续再定。
