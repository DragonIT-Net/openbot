# 前端 UI 改版计划

## 0. 需求

调整"千牛智能辅助"这个 WPF 桌面程序的所有前端页面（共 16 个 XAML 窗口/控件）的视觉样式，**不改变任何业务逻辑**。风格方向：现代简约风。具体问题不明确，按"全面翻新"处理。分两批推进：先做 1-2 个核心页面确认风格，再铺开到剩余页面。

设计参考已安装 `design-taste-frontend` skill（GitHub: Leonxlnx/taste-skill）。**如实说明**：这个 skill 面向的是网页营销页/落地页/作品集（React + Tailwind + Next.js + GSAP），并且它自己在"Out of Scope"里明确排除了 dashboard / 密集型产品 UI。本项目是 WPF 桌面客服工作台（多 Tab、多信息密度），技术栈也完全不同（XAML，无 Tailwind/GSAP/React 可用）。所以本次**不会照搬**它的具体代码套路，只借用其中语言无关的"设计品味"原则，翻译成 WPF 对应做法：

| Skill 原则 | WPF 落地方式 |
|---|---|
| 一个页面一个强调色（Color Consistency Lock） | 全局只定义 1 个 Accent Brush，替换现有蓝+黄两套配色 |
| 圆角/阴影一致性（Shape Consistency Lock） | 统一 CornerRadius 档位、统一柔和阴影（色调贴合背景，非纯黑） |
| 排版层级（Typography） | 统一字号/字重资源（标题、正文、次要文字三档） |
| 间距节奏 | 统一 4px 基准间距（4/8/12/16/24） |
| 交互状态完整（Loading/Disabled/Hover） | 复用现有 grdWaiting、Disabled 系列 Brush，只重新配色不改行为 |
| 审计先行、改版分级（Redesign Protocol §11） | 见下方"现状审计"，采用 "Redesign - Preserve"：只做视觉现代化，不动 IA/交互 |

## 1. 现状审计（Redesign - Preserve 模式）

- **配色不统一**：`App.xaml` 全局定义了 `tabLevel1`(蓝系)/`tabLevel2`(黄系) 两套 TabItem 样式，`RightPanel.xaml` 又自己定义了第三套 `tabRightPanel`(蓝系)。按钮默认全局样式是浅青色 `#FFF0FFFF`。三套配色并存。
- **圆角**：`tabLevel1/2` 有 4px 圆角，`tabRightPanel` 和大部分 Border/Rectangle 是直角，不统一。
- **阴影**：仅 `RightPanel` 外框用了默认 `DropShadowEffect`（纯黑阴影，未调色）。
- **间距**：Margin/Padding 数值随意（`5 1`、`5 3`、`10 5` 等），无统一节奏。
- **排版**：未显式设置字体/字号，全部走系统默认。
- **现有 Dial 估读**：VARIANCE 低（布局是标准系统控件堆叠）、MOTION 0（无动画/过渡）、DENSITY 中高（工作台类）。
- **必须原样保留（不能碰）**：
  - 所有 `x:Name`（`.xaml.cs` 里通过这些名字操作控件，如 `grdQnTab`、`ctlRightPanel`、`tabControl`、`grdWaiting`、`lblSeller` 等）。
  - 所有 `Click`/`MouseLeftButtonDown` 等事件绑定的方法名。
  - `Grid.Row/Column`、`TabControl` 的数据绑定结构、窗口的 `AllowsTransparency`/`WindowStyle`/`ShowInTaskbar` 等行为属性。
  - 三个拖拽热区 `Rectangle`（`rectWiden`/`rectHighden`/`rectCorner`）的 Cursor 和事件不变。

## 2. 设计方案

- 新建一个统一的设计资源字典（挂在 `App.xaml` 的 `Application.Resources` 里，或拆成单独文件用 `MergedDictionaries` 引入），包含：
  - **色板**：1 个主强调色（替换现有蓝/黄双色系），中性灰阶（背景/边框/次要文字/禁用态），语义色沿用现有 Disabled 三件套（`#EEE`/`#AAA`/`#888`）。
  - **圆角档位**：统一走"轻圆角"（如 6px），替换现有直角 Border/Rectangle 和 4px 的 TabItem。
  - **阴影**：柔和阴影（低透明度、贴合背景色调），替换 `RightPanel` 现有纯默认阴影。
  - **间距资源**：常用 Margin/Padding 数值统一到 4px 基准倍数。
  - **字体层级**：标题/正文/次要文字三档字号 + 颜色。
  - **统一 Button/TabItem 样式**：替换掉全局 `tabLevel1`/`tabLevel2`/`tabRightPanel` 三套模板为一套，`btnOption`/`btnSyn` 这类工具栏按钮改为 ghost 风格（不用纯色块）。
- 只改 XAML 里的外观属性（Background/BorderBrush/BorderThickness/CornerRadius/Margin/Padding/FontSize/FontFamily/Effect/Foreground），不改结构、不改 `.xaml.cs`。

## 3. 推进步骤

**第一批（本次先做，确认风格后再继续）：**
1. 在 `App.xaml` 里整理出统一设计资源（色板/圆角/阴影/间距/字体/Button 与 TabItem 样式），替换掉现有杂乱的 `tabLevel1`/`tabLevel2`/`scXxxBrush` 等。
2. [WndAssist.xaml](src/Bot/AssistWindow/WndAssist.xaml) — 主悬浮窗，主要是 `btnShowRight` 按钮套用新样式。
3. [RightPanel.xaml](src/Bot/AssistWindow/Widget/RightPanel.xaml) — 右侧面板：标题栏配色、`btnOption`/`btnSyn` 按钮样式、`tabRightPanel` 样式、外框阴影、加载条，全部套新设计资源。

**第二批（第一批确认后再推进，复用同一套设计资源）：**
- 机器人对话组件：`CtlRobot`、`CtlConversation`、`CtlImage`、`CtlOneGoods`
- 设置窗口：`WndOption`、`CtlRobotOptions`
- 托盘：`WndNotifyIcon`
- 通用弹窗：`WndInput`、`WndLoading`、`WndMsgBox`、`WndNoodles`、`WndNotTipAgain`、`WndRedBull`、`WndTrayTip`
- 表情选择器：`WndEmojiInputer`

## 4. 验证方式（重要限制）

当前开发环境是 macOS，无法编译/运行这个 .NET Framework 4.8 WPF 项目。我只能做到：
- 静态检查 XAML 语法正确性、`StaticResource`/`DynamicResource` 的 key 是否都存在、命名空间引用是否正确。
- 无法实际渲染截图确认效果。

**需要你在 Windows 上用 Visual Studio 编译运行后实际看一下效果**，如有偏差我再继续调整。

## 5. 待确认

- 主强调色具体选哪个颜色（比如沿用现有天蓝系柔化一版，还是换成别的），我会先给一版方案，你可以再调整。
