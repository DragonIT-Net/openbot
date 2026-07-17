# 引入 HandyControl 作为色板/资源来源

原计划（见 [plan.md](../../plan.md)）是手搓一套设计资源（色板/圆角/间距/字体）给现有 16 个窗口做外观。经讨论后改为引入 [HandyControl](https://github.com/HandyOrg/HandyControl)（NuGet 包 `HandyControl` 3.5.1，支持 net48）。原因：与其自己维护一套配色/画刷体系，不如接入一个成熟、有中文社区支持、还附带图标库和转换器等资源的现成方案。

**重要澄清（讨论中修正过一次）**：最初以为合并 HandyControl 的 `SkinDefault.xaml`（色值）+ `Theme.xaml`（画刷/样式）会像 MaterialDesignInXaml 那样**全局自动接管**原生 `Button`/`TabControl` 等控件外观。实际查阅 HandyControl 源码后确认：`Theme.xaml` 里只有 `Window` 是真正的隐式（无 `x:Key`）样式，`Button`/`TabControl` 等都需要显式引用具名样式键（如 `ButtonPrimary`、`TabControlCapsule`）才会生效，不会自动作用于项目里裸写的控件标签。

## Considered Options

- **手搓设计资源**（原计划）：完全自主可控，但需要自己维护圆角/阴影/间距等全部细节，且当时已有一套可用的 `ui*`/`sc*` 手写资源存在。
- **HandyControl 全局接管 + 逐控件替换具名样式**：真正用上 HandyControl 自带的各种 Button/TabControl 变体，但要改全部 16 个文件里每一个控件标签，工作量最大，与最初"不大幅改动"的诉求冲突。
- **自写隐式样式、颜色来源指向 HandyControl（采纳）**：保留项目里已有的 `App.xaml` 隐式 `Button` 样式和 `RightPanel.xaml` 的 `tabLevel1`/`tabLevel2`/`tabRightPanel` 自定义 `TabItem` 模板结构不变，只把这些样式引用的画刷（`uiAccentBrush`、`uiSurfaceBrush`、`uiBorderBrush` 等）的取值从写死的十六进制颜色改成 `{DynamicResource PrimaryColor}` 等指向 HandyControl 色板。改动范围最小：只需要动 `App.xaml`（合并资源字典 + 改画刷取值）和 `RightPanel.xaml` 里同样模式的本地画刷（`rpSkyBlueBrush` 等）；`WndAssist.xaml`、`CtlConversation.xaml`、`CtlRobot.xaml` 等已经引用这些资源键的文件完全不需要改动。

## Consequences

- `Bot/packages.config` + `Bot.csproj` 新增 `HandyControl` 3.5.1 依赖（`lib\net48\HandyControl.dll`）。
- `App.xaml` 合并 HandyControl 的 `SkinDefault.xaml` + `Theme.xaml`；已有的 `uiXxx`/`scXxx` 画刷改为引用 HandyControl 的 `PrimaryColor`/`BorderColor`/`RegionColor`/`PrimaryTextColor` 等色值资源；`ExpandCollapseToggleStyle`、`TreeArrow` 两个资源键名与 HandyControl 内部同名，但因为是直接声明在 `Application.Resources` 里（非 MergedDictionaries），按 WPF 资源解析规则会继续优先生效，不影响 TreeView 展开箭头的现有行为。
- `tabLevel1`/`tabLevel2`/`tabRightPanel` 的 `ControlTemplate` 结构不变（不套用 HandyControl 自带的 TabControl 模板），只是颜色来源统一到 HandyControl 色板。
- 只接入 Light 主题（合并默认的 `Colors.xaml`），不做深色模式切换。
- HandyControl 的全局隐式 `Window` 样式会对所有窗口生效；`WndAssist` 等已经显式设置 `WindowStyle="None"`/`AllowsTransparency="True"` 的窗口，本地属性优先级高于样式默认值，不受影响；其余未显式设置窗口样式的窗口外观可能跟着变化，需要在 Windows 上编译后逐个确认。
- C# 代码、`x:Name`、事件绑定、业务逻辑不受影响——这是纯表现层改动。
