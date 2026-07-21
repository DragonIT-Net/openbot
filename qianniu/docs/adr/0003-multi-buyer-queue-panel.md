# 多买家最新一轮对话汇总面板

同时接待多个买家时，客服只能看到"当前选中买家"的完整聊天记录（`CtlRobot` 的 `scvBody`），其余买家的最新动态即使已经由 AI 生成了回答，也要手动切换到千牛对应的买家窗口才能看到。新增一个常驻面板，展示每个买家的最新一轮问答，点击"确认"直接切到对应买家并按当前模式处理。

## Considered Options

- **替换现有"聊天记录" Tab 内容**：改动更集中，但会失去"当前买家完整滚动历史"这个视图。
- **新增独立常驻面板（采纳）**：保留原有单买家完整历史视图不变，新增一个横向汇总视图，两者并存，互不影响。

## 设计要点

- 面板位置：`CtlRobot` 顶部，买家信息条下方、Tab 条上方，不随 Tab 切换而隐藏。固定高度约 3 行，超出内部滚动（`ScrollViewer`），避免买家一多把面板撑得很长。
- 每行：买家昵称 + 最新一轮问答（单行截断）+ "确认"按钮；消息合并机制（见 ADR 0002）还没出结果时显示"回复中..."。
- 点击行原地展开显示该买家的历史轮次（存在 `BuyerQueueItem.PriorRounds` 里，不复用 `CtlConversation` 可视化气泡，避免同一个 WPF 元素被两处同时引用）。
- 点击确认：
  - 自动回复模式（该轮 `IsAutoReply == true`）：只调用 `QN.OpenChat` 切到千牛对应买家窗口，答案已经自动发送过了，不用再处理发送框。
  - 非自动回复模式：切换买家 + 调用 `PrepareTextAsync` 把答案填进千牛的发送框，等客服确认发送。
  - 处理完从队列里移除，直到该买家有新消息才重新出现。
- 数据流：`QN.cs` 的 `HandleBuyerMessageWithCoalescingAsync` 拿到 `burstProcessing` 锁、真正开始请求前，调用新增的 `Desk.MarkBuyerReplying(seller, buyer)`（跟现有 `Desk.AddConversation` 同款委托模式，内部 `DispatcherEx.xInvoke` 切到 UI 线程再转给 `CtlRobot`）显示"回复中"；请求结束后复用现有的 `desk.AddConversation(...)` 调用点，`CtlRobot.AddConversation` 内部同时更新队列面板。
- 已知的"同一批消息混了多个不同买家"的顺序处理隐患（见 ADR 0002 讨论）暂不处理，本次改动不涉及。
