# 汇总面板必须切换聚焦才能收到消息的修复

ADR 0003 上线后发现汇总面板并没有做到"不切换聚焦也能看到所有买家消息"：非聚焦买家的新消息实际上收不到内容，必须客服手动切过去才会出现在面板里。

## 根因

千牛推送新消息有两条通道：

- `receiveNewMsg`（挂在 `im.singlemsg.GetNewMsg` 任务完成上）：带完整消息内容，但只在千牛自己的 UI 主动拉取时触发，实测只对**当前聚焦**的会话触发。
- `onShopRobotReceriveNewMsgs`（挂在 `im.singlemsg.onReceiveNewMsg` SDK 事件上）：对**所有**会话都会触发，但 `inject.js` 里原来的 `getRemoteMsg` 虽然通过 `im.singlemsg.GetRemoteHisMsg` 把完整消息内容拉下来了，却只从里面摘出发件人身份（`fromid`）就把内容扔了，只把身份传回 C# 端。

且这个拉取还只在"买家身份没缓存过"时才会调用（`if(conv == undefined)`）——同一个买家聊过一次、身份进了缓存之后，后续新消息完全不会再触发这次拉取，连身份都是走缓存返回的旧值。

## Considered Options

- **让 `receiveNewMsg` 对所有会话都触发**：改动更底层，千牛内部任务调度机制不透明，风险和不确定性都更高。
- **让 `onShopRobotReceriveNewMsgs` 把已经拉到的消息内容带出来，不再按缓存决定要不要拉（采纳）**：`onShopRobotReceriveNewMsgs` 本来就对所有会话触发，`GetRemoteHisMsg` 也已经把内容拉下来了，只是被代码扔掉——把这部分内容保留、去掉缓存短路即可，改动集中在 `inject.js` 一个函数 + C# 侧多传一个字段，不涉及新的千牛内部 API。

## 改动内容

- `inject.js` / `Bin/inject.js`（两份保持同步）：`getRemoteMsg` 返回 `{ buyer, msgs }`（`msgs` 是 `GetRemoteHisMsg` 返回的最近 3 条消息），`onReceiveNewMsg` 回调不再判断缓存决定要不要拉，每次都拉，`msgs` 随 `onShopRobotReceriveNewMsgs` payload 一起传给 C# 端；买家身份仍然优先用本次拉取结果，取不到再退回缓存、再退回裸 `{ ccode }`，保留原有兜底行为。
- `CDPEventArgs.cs`：`ShopRobotReceriveNewMessageEventArgs` 新增 `Messages`（`List<QNChatMessage>`）字段。
- `CDPClient.cs`：`ShopRobotReceriveNewMessage` 改用一个新增的私有 payload 类（`ShopRobotMessagePayload`）解析，多解析出 `msgs` 字段。
- `QN.cs`：把 `Cdp_EvRecieveNewMessage` 里"推送聊天记录 + 逐条处理买家消息（去重/进店提示过滤/订单查询/消息合并）"这部分抽成共用方法 `ProcessIncomingMessagesAsync`/`ProcessIncomingBuyerMessageAsync`，`Cdp_EvShopRobotReceriveNewMessage` 收到 `e.Messages` 后调用同一套方法处理。两条通道共用同一套去重逻辑（按 ccode+clientId+messageId 判重），如果同一条消息之后因为切换聚焦又走 `receiveNewMsg` 收到一遍，会被去重挡掉，不会重复触发 AI 或重复发送。

## Consequences

- 每次任意会话来新消息，都会多一次 `GetRemoteHisMsg` 调用（`count:3`），比原来（只在首次遇到某买家时调用）请求更频繁，但拉取的是轻量的最近 3 条历史，可接受。
- 自动回复模式下"收到通知就 `OpenChat` 切过去"这个原有行为没有变——因为实际发送消息（`SendTextAsync`）在千牛新版本下走 RPA/UI 自动化，需要目标买家会话处于聚焦状态才能操作；这次改动只是让"生成回答"这一步不用等聚焦切换就能开始，没有改变"发送"这一步仍需切换聚焦的前提。
- ADR 0002 里提到的"同一批消息混了多个不同买家"的顺序处理隐患依旧不在本次改动范围内。
