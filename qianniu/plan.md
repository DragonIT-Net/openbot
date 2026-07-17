# 前端 UI 改版计划

## 0. 需求

调整"千牛智能辅助"这个 WPF 桌面程序的所有前端页面（共 16 个 XAML 窗口/控件）的视觉样式，**不改变任何业务逻辑**。

引入 [HandyControl](https://github.com/HandyOrg/HandyControl) 作为色板/资源来源。决策过程、澄清过的误解和最终取舍见 [docs/adr/0001-handycontrol-as-ui-foundation.md](docs/adr/0001-handycontrol-as-ui-foundation.md)。

## 1. 已完成（本轮）

- `Bot/packages.config` + `Bot.csproj` 加入 `HandyControl` 3.5.1 依赖。
- [App.xaml](src/Bot/App.xaml)：合并 HandyControl 的 `SkinDefault.xaml` + `Theme.xaml`；已有的 `uiXxx`/`scXxx` 画刷改为引用 HandyControl 色板（`PrimaryColor`/`BorderColor`/`RegionColor`/`PrimaryTextColor` 等），保留原有的隐式 `Button` 样式和 `tabLevel1`/`tabLevel2` 模板结构不变。
- [RightPanel.xaml](src/Bot/AssistWindow/Widget/RightPanel.xaml)：本地的 `rpSkyBlueBrush`/`rpWhiteBrush`/`rpDodgerBlueBrush` 同样改为引用 HandyControl 色板。
- `WndAssist.xaml`、`CtlConversation.xaml`、`CtlRobot.xaml`、`CtlRobotOptions.xaml` 等已经引用 `uiXxx` 资源键的文件**未做改动**，颜色会随 `App.xaml` 里资源定义的变化自动联动。

## 2. 现状审计（仍然有效的背景信息）

- **必须原样保留（不能碰）**：
  - 所有 `x:Name`（`.xaml.cs` 里通过这些名字操作控件，如 `grdQnTab`、`ctlRightPanel`、`tabControl`、`grdWaiting`、`lblSeller` 等）。
  - 所有 `Click`/`MouseLeftButtonDown` 等事件绑定的方法名。
  - `Grid.Row/Column`、`TabControl` 的数据绑定结构、窗口的 `AllowsTransparency`/`WindowStyle`/`ShowInTaskbar` 等行为属性。
  - 三个拖拽热区 `Rectangle`（`rectWiden`/`rectHighden`/`rectCorner`）的 Cursor 和事件不变。
  - `tabLevel1`（[WndOption.xaml.cs](src/Bot/Options/WndOption.xaml.cs)）、`tabRightPanel`（[RightPanel.xaml.cs](src/Bot/AssistWindow/Widget/RightPanel.xaml.cs)）这两个资源键名被 `.cs` 代码通过 `FindResource("键名")` 按字符串查找，键名本身不能改，只改了键对应的内容。

## 3. 已知风险 / 待 Windows 验证的点

- HandyControl 的全局隐式 `Window` 样式会对所有窗口生效；`WndAssist` 已显式设置 `WindowStyle="None"`/`AllowsTransparency="True"`，本地属性优先级更高，预期不受影响；其余没有显式设置窗口样式的窗口（`WndOption`、各种 `Wnd*` 弹窗）外观可能会跟着变，需要逐个打开看一眼。
- `ExpandCollapseToggleStyle`、`TreeArrow` 这两个资源键名跟 HandyControl 内部同名，但因为是直接声明在 `Application.Resources`（不在 MergedDictionaries 里），按 WPF 规则会继续优先生效，TreeView 展开箭头预期行为不变，但建议实际打开一个用到 TreeView 的界面确认一下。
- HandyControl 的 `Window` 样式可能引入默认的最小化/最大化/关闭按钮样式或窗口圆角，需确认跟现有窗口的自定义标题栏/拖拽逻辑（如果有）不冲突。

## 4. 验证方式（重要限制）

当前开发环境是 macOS，无法编译/运行这个 .NET Framework 4.8 WPF 项目。已完成的检查：
- 静态检查了 XAML 语法、`StaticResource`/`DynamicResource` 引用的 key 是否都存在（含手动核对 HandyControl 源码确认 `PrimaryColor`/`BorderColor` 等色值资源键名和 `ExpandCollapseToggleStyle`/`TreeArrow` 的键名冲突不影响功能）。
- 无法实际渲染截图确认视觉效果，也无法验证 NuGet 包能否正常还原、能否编译通过。

**需要你在 Windows 上用 Visual Studio 打开解决方案、还原 NuGet 包、编译运行，重点看一下第 3 节列的几个风险点。**

## 5. 后续（未做，视效果决定要不要继续）

如果这次颜色统一后效果不够"现代"，可以再往下走：给圆角、间距、阴影做进一步调整，或者针对个别弹窗（`WndMsgBox`/`WndTrayTip` 等）引入 HandyControl 自带的 Growl/Dialog 控件替代手搓窗口——但这两者都是尚未讨论细节的新范围，需要另外过一轮。
