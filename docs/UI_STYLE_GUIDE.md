# nomozip 0.3.1 UI 修改入口

0.3.0采用的界面延续至0.3.1，新增共享按钮内容遮挡修复，已获用户授权发布。以后在既有样式内扩展时，从本页开始；历史候选和早期概念仅供比较，不能覆盖当前基准。

## 基准与优先级

1. 当前运行资源：`src/Desktop/App.xaml`、`ThemeResources.cs`、`WindowChromeResources.xaml`、`ActionMenuResources.xaml`和各页面XAML。
2. [采用稿、补充参数和验收历史](../design-system/reference-20261017/MASTER.md)，按最新阶段覆盖旧阶段；[实际截图对照](../design-system/reference-20261017/index.html)。整体布局基准3a4a6cc，按钮修复07632d7；0.3.1冻结源码见v0.3.1标签。
3. [从当前源码提取的资源快照](../design-system/reference-20261017/release-0.3.0/resources.json)，记录命名样式、原始Setter、颜色和来源文件SHA；它保留0.3.0原始检索快照；0.3.1按钮模板按本页和button-content-20261018实际原图补充，修改应回到WPF资源。

参数冲突时先查看该控件当前实现与对应实际截图。原型布局不能强制覆盖后续已经确认的密码库、表单和极小窗口修正。改变已确认方向，先提交具体设计供用户确认。

## 延续的样式

| 项目 | 0.3.0依据 |
| --- | --- |
| 平台 | WPF；共享.NET 10 Windows Desktop Runtime；x64 / ARM64、简中 / 英语和系统高对比 |
| 窗口 | 主客户区1152×768 DIP、最小800×600；归档工作区页头130、底栏49；独立工具另有小窗口规则 |
| 字体 | Microsoft YaHei UI → Segoe UI → Microsoft YaHei → Global User Interface；品牌使用Segoe UI，不下载字体 |
| 正文与标题 | BodyControl/BodyText为15 DIP、Normal；正文行高22。WorkspacePageTitle为26/Bold/38，PageTitle为22/SemiBold/32，Caption为13/20；特殊行/字段取对应资源，不能统一强制成正文 |
| 主色 | 浅色AccentBrush #006CD9，CanvasBrush #F3F4F6，SurfaceBrush #FFFFFF，TextBrush #111722；其他语义色及深色/柔和色取资源快照和ThemeResources |
| 控件 | 主操作胶囊36 DIP高，图标通常20×20，图文间8/10；输入框圆角4，焦点使用FocusBrush的2 DIP边框；间距以8/12/16/20/24为主 |
| 表单 | 短选项按当前文字自然宽度、最小80 DIP，保留箭头右留白；格式/级别同排、间隔20；路径/名称在最大640 DIP表单内弹性编辑；自定义数字128+间距8+单位90 |
| 图标 | 只保留原nomozip主图标；功能统一Fluent Regular 20。原SVG、WPF几何、许可证及来源在assets/fluent；不要混用emoji、字体符号或其他视觉重量 |
| 状态 | 悬停、按下、禁用、键盘焦点沿用共享模板；表头使用独立矩形模板，不让焦点/选中背景铺满整列；高对比优先系统语义资源 |
| 动画 | 任务圆环/进度条/百分比绑定同一显示进度，220ms CubicEaseOut，0/100直接到位；暂停同步停止，尊重减少动态效果 |
| 长内容 | 名称/路径单行省略并提供完整提示；输入值保持可编辑；长说明换行；多条目纵向滚动。窄窗按现有页面断点隐藏次要列/收起预览，不压缩主要动作 |

按钮的悬停/按下背景必须位于文字和图标下方；共享模板直接更换承载内容的ButtonSurface背景，不能用不透明的上层覆盖层。状态验收需要真实鼠标进入、按下，以及最终截图中文字/图标的可见性；正常态或Content属性存在不能代替悬停验收。

小窗口标题区必须留出系统关闭按钮，并确保位于WindowChrome区域的搜索/操作接受客户区输入。详细阈值和留白见MASTER文首，原图在`regression-3a4a6cc/`。

## 复用位置与检查

- 通用字体、按钮、字段、列表、表头、焦点：`src/Desktop/App.xaml`。主题语义色：`ThemeResources.cs`。窗口/菜单：`WindowChromeResources.xaml`、`WindowChromeBehavior.cs`、`ActionMenuResources.xaml`。
- 页面：`MainWindow.xaml`及Workspace分部、`CompressionView.xaml`、归档浏览/预览、密码与主密码、校验和其他对话框。进度：`TaskProgressView`；图标：`FluentIcon`和`FileKindIcon`。
- 同一功能优先复用现有命令、ViewModel和资源，不复制固定宽度/颜色模板。检查空、典型、长路径、多条目、处理中、暂停、成功和失败等适用状态。
- 修改后在隔离CI运行受影响检查，以相同窗口尺寸、语言、主题、内容和状态截取实际软件图，和对应基准并排/必要时叠加。核对文字、字体、图标、颜色、间距、尺寸与输入状态；复用未变更的有效证据。禁止本机桌面自动化、读取真实密码库或替换实际安装。

## 历史资料与下载

- 待选1：`design-system/next-20261017/`，可运行源码`prototypes/UiNext/`，分支`codex/ui-next-20261017`。
- 待选2：`design-system/option2-20261017/`，可运行源码`prototypes/UiOption2/`，分支`codex/ui-option2-20261017`。此版未采用。
- 最终采用：`design-system/reference-20261017/`，原型`prototypes/UiReference/`，产品分支`codex/ui-reference-20261017`。原型候选不是完整软件。
- 更早已撤回的UI：`concepts-2026-10-16/`、`implementation-review/`及`codex/ui-redesign-20261016`历史分支，保留作参考，不作为发布依据。
- [候选清单](../design-system/UI_CANDIDATES.json)逐项记录原始ZIP、源码提交、SHA256与状态。标注被替代/未采用的包仅供历史复现，不宣称通过当前完整验收。

[0.3.0发行附件](https://github.com/Nomoziya/nomozip/releases/tag/v0.3.0)同时保存设计资料ZIP、原始历史候选ZIP合集、可恢复Git源码历史bundle及回归证据。SHA256SUMS覆盖这些附件。Git bundle可用`git clone <文件> nomozip-history`在新目录还原，再切到清单对应提交；候选使用独立测试配置，不能覆盖正式安装或真实库。


## 隐私清理后的历史源码查找

2026-10-19经用户授权，公开历史中的两个脚本改为USERPROFILE相对查找，不再保存本机用户名路径。原始CI提交、截图来源和候选文件名保持不变；[HISTORY_COMMIT_MAP.json](../design-system/HISTORY_COMMIT_MAP.json)给出336项新旧提交映射。候选清单的sourceCommit保留原值，cleanSourceCommit可直接检出脱敏后的等价源码；原始43个ZIP的SHA不变。

0.3.1原CI构建为d26e6f4，清理后的发行标签指向537237e，两者源文件树完全一致；程序的信息版本后缀保留原CI身份。0.3.0历史bundle已替换为脱敏版本，下载时同时保留发行附件中的nomozip-history-commit-map-20261019.json。原始未脱敏备份仅本地保存，禁止从旧工作区重新推送旧refs。
