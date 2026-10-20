# nomozip 0.3.2 UI 修改入口

## 0.3.2 后续扩展（2026-10-20，开发验收中）

软件更新卡片在缓存行后增加自动清理复选框和显式保存，默认开启；文字可换行，保存入口随WrapPanel排列。下载沿用原进度条/状态行显示MB、KB/s及停滞说明，无数据时仍可取消。归档浏览的动作区下增加默认折叠高级筛选：扩展名输入180、MiB上下限各140 DIP（可输入小数）、条目类型使用CompactOptionField自然宽度，WrapPanel在800宽下换行；错误与范围说明换行，普通表格/字体/主题沿用。搜索文本绑定200ms延迟，显式命令及ViewModel更新同步。密码详情和编辑对话框原规则折叠区增加1024字符来源目录字段及说明，使用原滚动区域、Label.Target与可访问名称，不增加新配色/图标。最终截图及边界以本批CI验收为准。


## 0.3.2 正式基准（2026-10-19）

用户已授权将功能与官方源候选发布为0.3.2，发行冻结7bac1a5。下方候选阶段的待验收描述保留为历史快照；后续修改以v0.3.2运行资源、两组provenance及实际原图为基准，沿用0.3.1按钮修复。主布局/字体/配色方向不变。正式包CI39251599209及公开附件核对见[当前摘要](CURRENT_STATE.md)；物理设备剩余验收范围不因此自动通过。

## 官方引擎源扩展（2026-10-19，候选完成待用户验收）

更新设置的引擎卡片中，版本摘要之后增加带可访问名称的来源ComboBox。复用CompactOptionField，按中英文选项文字自适应（MinWidth80、Height沿用表单、HorizontalAlignment Left），标签右距12、底距8 DIP。说明和状态均复用Secondary及主题资源，支持换行；状态保留Polite通知。默认“nomozip 已验证源”，另选“7-Zip 官方源”；切换后清除已发现包并提示重新检查。自定义仓库仅作用于签名源，官方源下禁用；来源在检查更新或保存设置时持久化。管理版本、确认、取消及回退沿用既有入口，不改变导航或主视觉；官方源说明明确实际经GitHub。验收覆盖800×600/1152×768、中英、浅深及失败/成功，最终实际截图对应候选源码。

[双架构实际原图、阶段验收与剩余范围](../design-system/reference-20261017/official-engine-20261019/index.html)以f2ec68a为候选来源；provenance.json记录56张原图、24份原报告及SHA。新增控件使卡片变高并出现正常垂直滚动条；沿用1 DIP集成边框和原有字体/配色/焦点资源。技术检查通过不代替用户确认。

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

## 开发分支扩展

开发分支规范内扩展（2026-10-19，尚未验收/发布）：任务行“暂停/继续”保持172/196 DIP操作列及36 DIP按钮，完整作用范围放在同一Fluent菜单和提示；全局按钮仍写“队列”。暂不适用的新菜单项隐藏，避免一律扩长菜单。密码列表底部新增备份状态行，内边距16×8 DIP、顶部1 DIP分隔线，不占用右侧详情高度，13 DIP辅助文字，需提醒时使用现有AccentBrush；右侧36 DIP导出按钮随文本自然宽度。软件更新状态之前新增缓存清理按钮、数量/大小辅助文字，窄窗WrapPanel换行；清理中禁用重复操作，结果以真实数量与释放字节格式化显示。所有背景沿用内容下方共享模板，暂停动画取任务实际确认及队列状态。暂停用Fluent Pause、继续用Fluent Play，同样20×20、图文间8 DIP，行/详情/队列入口一致。更改入口：MainWindow.xaml、MainViewModel.Queue/BackupReminder/ApplicationUpdates、TaskProgressView，颜色与字型继续从原资源取值。[08bce29双架构实际截图、阶段验收记录与剩余差异](../design-system/reference-20261017/task-improvements-20261019/index.html)已保存；自动检查通过，仍待用户确认，未替代0.3.1正式基准。1152×768和800×600按WPF集成窗口渲染尺寸记录，含1 DIP边框；内部内容分别为1150×766和798×598。

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
