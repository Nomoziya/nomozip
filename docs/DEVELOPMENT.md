# 构建、验证与维护

第七预览（2026-10-12）补齐内容浏览界面，代码提交 `3fc065e2b0e77d262a6731de72a93b3914471a95` 的 [Windows CI 38239454113](https://github.com/Nomoziya/nomozip/actions/runs/38239454113) 全部通过。两版实际引擎浏览 28/28，桌面浏览 13/13（包括自动/手动文件名加密、直接查看 AES ZIP、搜索及空匹配、键盘名称/快捷键、小工作区完整文件行及数据不被浏览修改），既有布局 44/44；原解压、压缩、密码库和更新流程保留通过。引擎矩阵已扩展为 79/79，并由现有官方维护脚本对未来候选执行；签名工具拒绝缺少目标版本列表覆盖的报告，拒签测试通过。十四份报告和 35 张虚构数据渲染位于 `artifacts/github-validation-38239454113/validation-reports/`。完整安装 17/17、完整到轻量安装 18/18，云端轻量安装 9244171 字节，确认共享运行库和旧密码库复用。审核载荷轻量 9043567 字节 / 43 文件、完整 155200322 字节 / 438 文件；轻量 EXE 2086719 字节、ZIP 3328492 字节，约 9 MB 仍不包含系统运行库。浏览不写入输出目录/密码库，不将打开头部误当作数据验证成功；其实际体验和未完成人工桌面项目继续待确认。以下旧版本记录保留各自范围。

第六预览已公开：[v0.1.0-preview.6](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.6)，标签提交 `fa20f300d337b212166e99ee8a1fcf71e1796888`，仅比下述通过 CI 的代码提交增加文档记录。轻量安装 EXE 2081691 字节、ZIP 3318677 字节；完整 EXE/ZIP、86 文件源码快照和 SHA256SUMS 也已发布。六项资产经公开无登录地址重新下载，SHA-256 与本地及 GitHub 资产摘要一致，报告 `artifacts/public-preview-6-validation.json`；包 CRC 与私密材料排除检查通过。轻量/完整 worker、26.04/26.03 引擎和固定公钥逐字节一致，指标报告 `artifacts/package-size-validation.json`。稳定引擎仍为 `engine-26.04`，旧发布未覆盖，本机入口已更新。

最新进度（2026-10-12）：第六预览的压缩/密码库一致性修整完成，代码提交 `bee2f344e663d95d3a9eda527733d115eeea629f` 的 [Windows CI 38236148792](https://github.com/Nomoziya/nomozip/actions/runs/38236148792) 全部通过，十二份报告和 34 张虚构数据渲染位于 `artifacts/github-validation-38236148792/validation-reports/`。44 项桌面检查覆盖宽/窄窗口主操作固定、分栏一致性、17 条虚构密码记录的行高/对齐和列宽，其余既有引擎、密码库、更新、压缩、打开方式、四条桌面流程、签名和完整/轻量安装检查均通过。局部本地安装 18/18，通过实际复用旧虚构密码库与共享 .NET 10.0.12，安装 9189724 字节；云端轻量安装为 9213600 字节。轻量载荷 9015576 字节 / 43 文件，完整载荷 155172327 字节 / 438 文件。用户已确认第五预览的解压布局/约 9 MB 体积；第六预览压缩和密码库的新布局仍待体验，真实桌面未确认项目继续保留。下文旧版记录按其原版本解释。

当前交付为 0.1.0-preview.7 开发预览，可运行的 Windows x64 WPF 软件。密码复用、本地签名更新、真实 GitHub 线上引擎更新及安装/升级/卸载已验证；内容浏览、压缩和密码复用均已实现，完整人工桌面验收仍待完成。

第二预览增加当前用户安装包与携带运行组件的独立 ZIP。构建和安装/升级/卸载验收见 [安装说明](INSTALLATION.md)。普通开发构建继续使用系统运行时；`-SelfContained` 构建会携带 .NET / WPF 运行组件。安装器不会将这些组件安装为全局运行时。

第二预览提交为 `50df33855e25b8daa9747b20335efabf3eb0ab17`，其 [GitHub Windows 验证](https://github.com/Nomoziya/nomozip/actions/runs/38202511444) 包含 14/14 安装检查，覆盖升级后真实复用旧 DPAPI 密码库、实际加载安装目录中的运行组件、离线解压、数据保留与明确删除。下载地址为 [v0.1.0-preview.2](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.2)。

## 本机运行

运行 `artifacts/app/ArchivePassword.exe`。需要 .NET 10 Windows Desktop Runtime；当前本机已有对应运行时。此包目前不是自包含安装包。

使用“添加压缩包”或拖放文件，选择输出目录，点击“开始解压”。密码库有可用密码时自动尝试，否则请求输入；输入界面可勾选成功后保存。密码库默认位置为 `%LOCALAPPDATA%/ArchivePassword/passwords.apv`，由当前 Windows 用户加密保护。解压结果通过“打开输出目录”查看。

文件名加密 7z、普通/加密 ZIP、RAR 的真实样本已经过引擎验证。同一压缩包包含多个不同密码时，当前引擎能报告受影响条目，产品暂不支持组合多条密码完成解压；不要将反复请求密码当成密码库丢失。

## 可复现构建

在项目目录运行以下 PowerShell 脚本：

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1
./scripts/test.ps1
```

Bootstrap 将官方两版引擎、源码、JSON 头文件、DPAPI NuGet 包和便携 SDK 放入项目内；使用本机的 7z 与 MinGW g++。SDK 锁定于 global.json，本轮使用 10.0.401。构建采用单节点、禁用共享编译服务器，在本机权限环境下已通过。没有修改系统 PATH 或替换系统 7-Zip。

Python 测试在本轮使用 Codex 自带 Python。其它机器需可用 Python 3，或调整脚本中的 Python 路径。SDK 与工具下载、测试夹具、运行产物不纳入源码跟踪。RAR 测试夹具只用于集成测试，不进入产品包。

## 官方更新跟进

```powershell
./scripts/follow-official-engine.ps1
./scripts/follow-official-engine.ps1 -Update
```

第一条只检查官方稳定版；第二条在有新版本时下载官方包，禁用候选引擎，运行解压和压缩兼容性矩阵，通过后重新生成本地产品包。26.04 已在主项目中，因此普通维护运行无需更新；2026-10-12 已在隔离工作区将其作为缺失候选，实跑完整下载/验证/重建及失败保护分支。真实未来版本的具体 DLL 尚未出现，仍须逐版验收。

此脚本面向项目维护与本机开发。现有产品可检查官方版本和产品 GitHub Releases、下载或导入签名更新包、验证后启用，并恢复上一引擎。更新包不需要用户另装官方 7-Zip。流程、测试包和发布密钥约定见 [更新说明](UPDATES.md)。源码和 CI 已上传，软件下载及稳定引擎包见 [Releases](https://github.com/Nomoziya/nomozip/releases)。

`.github/workflows/windows-validation.yml` 已部署 Windows 构建、实际引擎/密码库/更新/桌面验证，以及定期检查官方新引擎的流程；[首次 GitHub 运行](https://github.com/Nomoziya/nomozip/actions/runs/38197882961) 全部检查通过。流程仅保留验证报告，不发布产品，也不使用正式签名密钥。

公开发布后，维护者可在已构建程序上执行真实线上更新验收：

```powershell
$taskApp = Start-Process artifacts/app/ArchivePassword.exe -ArgumentList @('--validate-online', $PWD.Path) -WindowStyle Hidden -Wait -PassThru
if ($taskApp.ExitCode -ne 0) { throw 'Online acceptance failed; inspect artifacts/online-validation.json.' }
```

该模式使用独立的虚构密码库，通过正式桌面命令从 26.03 检查、下载、校验和启用公开发布的 26.04，验证实际解压与回退持久化。它不随普通 CI 自动执行，也不访问用户真实密码库。

## 本轮验证范围

- 57 项引擎检查全部通过：两版引擎、真实输出内容、错密、密码请求、7z 文件名加密、传统与 AES ZIP、RAR/RAR5、多密码包部分失败、越界路径、损坏包、缺分卷及原生/托管取消。
- 17 项密码库与流程检查全部通过：整体加密、重新打开、排序/去重、禁用/删除、自动和手动解压、成功保存、同名处理、跳过候选、损坏恢复和未知数据版本保护。
- 22 项更新检查全部通过：有效签名、哈希及身份拒绝、包路径拒绝、不兼容协议、无法运行的引擎、准备清理、取消、安装与回退、重新打开、已安装文件篡改与未知选择记录保护。GitHub 发布选择和下载接入同一签名校验路径使用模拟 HTTP 响应；不声称线上发布已通过。
- 首次稳定引擎发布后，真实 GitHub 公开网络的 8 项桌面检查通过：从 26.03 下载发布的 26.04、验签与安装、实际密码解压、密码库保护、回退和重开持久化。报告为 `artifacts/online-validation.json`；此前模拟 HTTP 报告仍保留其独立范围。
- 3 条实际 WPF 流程通过：自动复用已保存密码；密码库为空时，通过真实密码对话框输入并勾选保存；通过正式更新命令导入、启用 26.03、实际解压、回退 26.04 并重新读取选择记录。输出与测试文件逐字节一致；更新期间密码库文件保持不变。测试数据和临时测试公私钥在项目 artifacts 目录中，测试应用采用独立副本，不修改产品信任公钥或使用用户真实密码库。
- 已检查三个实际 WPF 页面和 800 DIP 宽度的渲染预览；渲染数据为虚构数据。真实键盘、Narrator、高对比度、多屏 DPI 与用户实际使用尚待验收。

详细报告：`artifacts/engine-validation.json`、`artifacts/core-validation.json`、`artifacts/update-validation.json`、`artifacts/desktop-validation.json`。用量与完整产品质量不从这些局部检查推导。

首个公开预览 [v0.1.0-preview.1](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.1) 对应提交 `738ebf5e274f01134114468cabcbf3f35f7a2255`，其 [GitHub Windows 检查](https://github.com/Nomoziya/nomozip/actions/runs/38198753411) 通过。软件下载资产已通过公开地址重新下载并核对 SHA-256。发布资产保持不可变；修改代码后使用新的版本标签，勿覆盖旧包。

## 第三预览验证

2026-10-12 的 P3 增加 7z/ZIP 创建、压缩级别、加密和 7z 文件名加密。两版官方引擎的 38 项压缩检查通过，覆盖官方工具读取、空文件与目录、错密、覆盖保护、取消和失败清理。实际 WPF 按钮通过 UI Automation 创建加密包，输入并保存密码，再用密码库自动解压，输出逐字节一致；桌面流程扩展为四条，控件名称与页面快捷键检查通过。7 项隔离注册表检查通过，验证可选打开方式登记/移除不会修改默认应用或删除其他程序、副本的登记。

官方引擎跟进脚本与签名打包器现在同时要求解压和压缩报告，报告必须绑定实际 DLL 哈希；错配压缩报告的拒绝签名检查通过。不会因此覆盖已有稳定引擎发布资产。安装/升级/卸载的 16 项检查通过，覆盖密码库与设置保留、安装内运行组件、旧密码实际复用、默认卸载保留与明确删除数据、卸载只清理自己的打开方式登记。

第三公开预览 [v0.1.0-preview.3](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.3) 对应代码提交 `fc1c162b46b977aa89895a09d7b6b0aaced87cd5`；[Windows CI 38206962560](https://github.com/Nomoziya/nomozip/actions/runs/38206962560) 全部通过，九份报告下载至 `artifacts/github-validation-38206962560/validation-reports/`。分发包含安装 EXE、438 文件自包含 ZIP、83 文件源码快照与校验清单，旧预览和稳定引擎资产保持不可变。四项发布资产均通过无登录公开地址下载并核对本地及 GitHub SHA-256 摘要，稳定引擎渠道仍为 `engine-26.04`；报告为 `artifacts/public-preview-3-validation.json`。

新增报告为 `artifacts/compression-validation.json`、`artifacts/association-validation.json` 和 `artifacts/signing-validation.json`。UI Automation 检查不能代替真人键盘、Narrator、高对比度、DPI 和 Explorer 入口验收；同包多密码组合解压仍未实现。7z 支持 Unicode 密码，当前官方 ZIP 写入接口只接受 ASCII 密码，界面会明确拒绝不支持的输入。

## P5 无障碍与布局验收

2026-10-12 用户确认第四预览的“压缩加密、自动解密，目前没发现问题”，已记录为压缩加密和自动复用密码解压的人工通过。报告未指定归档格式及其它操作，不扩展为完整人工验收；第五预览的新布局/轻量安装及键盘、Narrator、DPI、Explorer 等仍待确认。用户随后确认第五预览的解压布局符合预期、安装体积约 9 MB，将这两项追加为人工通过；旧记录保留及其余实际桌面操作仍未确认。此前自动报告保留原检查范围，不将其人工验收字段改为通过。

对比第四最终提交 `3bf9c4443124244ffbf524737ffde373034f2997` 与第五最终提交 `ddb3771c8f27e3fc212a3cf143ee1b79945744ef`，`src/Core`、`src/Infrastructure` 和 `src/EngineWorker` 无源码变化，保留用户已经通过的功能验收，不因后续阶段而重复这些检查。

第四预览修复系统高对比度资源下选中导航和提示的文字配色、选中归档辅助文字的颜色继承，以及开启“显示密码”后提交空密码应返回可见输入框的问题。高对比度开关变化会在应用运行时更新资源，关闭后恢复原设计配色。主窗口启动和密码对话框按系统工作区收缩最小尺寸与初始尺寸，避免高缩放下固定高度超过可用区域。

新增 `artifacts/accessibility-validation.json` 和 `artifacts/accessibility-preview/`：29 项检查覆盖四页在 1120×760 与 800×500 DIP 下的可获焦输入控件名称、滚动可达性、主窗口和密码对话框适应较小工作区、选中行文字、系统色资源映射与恢复、密码框受保护语义、显示明文提示及空密码的逻辑焦点。34 张渲染图使用虚构数据和 100%/125%/150%/200% 像素密度；高对比度图使用系统色资源模拟，不修改操作系统主题。报告明确保留 `humanKeyboardNarratorExplorerAndMonitorDpiAccepted=false`，不把控件属性、程序焦点或离屏渲染称为真人验收。

人工体验剩余步骤以 [第七预览人工验收](INSTALLATION.md#第七预览的人工验收) 为准：先确认压缩页和多条记录后的密码库新布局，再补齐旧记录保留、键盘、讲述人、实际系统主题/缩放和 Explorer 入口。第四预览已通过的压缩加密和自动解密保留有效，不要求重复验收。安装、升级、卸载的数据保留已自动验证，实际体验仍需用户确认；未测试环境明确保留为未验证，P5 保持未完成。

实现依据（2026-10-12）：微软的 [WPF 焦点说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview) 区分键盘焦点与逻辑焦点；[系统资源说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-how-to-use-system) 和 [系统参数变更事件](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.staticpropertychanged?view=windowsdesktop-10.0) 支持系统配色更新；[WorkArea](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.workarea?view=windowsdesktop-10.0) 提供主显示器工作区，跨显示器场景仍待实际验收。

2026-10-12 第四公开预览 [v0.1.0-preview.4](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.4) 对应最终提交 `3bf9c4443124244ffbf524737ffde373034f2997`；[CI 38223276557](https://github.com/Nomoziya/nomozip/actions/runs/38223276557) 全部通过，原有检查及新增 29 项桌面检查均通过，十份报告和 34 张渲染图下载至 `artifacts/github-validation-38223276557/validation-reports/`。安装 EXE、438 文件自包含 ZIP、86 文件源码快照与校验清单四项资产已从无登录公开地址下载，SHA-256 与本地和 GitHub 摘要一致；报告为 `artifacts/public-preview-4-validation.json`。稳定引擎通道仍为 `engine-26.04`，旧发布资产保持不变。本机 `artifacts/app/ArchivePassword.exe` 同步为第四预览代码；人工验收状态按上节继续保留。

## 第五预览：实际体验修复与轻量分发

解压页固定开始、取消、跳过密码尝试和打开输出目录操作，普通宽窗口设置卡片无需滚动。窄窗口使用外层区域滚动，设置内部不再另设滚动；统一细轨样式保留 WPF 原有 Track/Thumb 和页面滚动命令，未隐藏溢出设置。补充桌面检查扩展到 34 项，包含主操作位置、滚动结构和真实 WPF 滑块拖动事件使偏移变化。四条桌面功能流程继续通过；渲染使用虚构数据，不能代替用户的新一轮体验。

用户选择保留 WPF 和功能，新增共享 .NET 10 Desktop Runtime 的轻量分发；完整独立分发保留。原完整载荷约 148 MB，轻量载荷约 9 MB；后者不包括系统共享运行库的安装占用。安装、依赖、从完整旧版清理载荷并保留密码/设置/文件入口的行为见 [INSTALLATION.md](INSTALLATION.md)。隔离的完整到轻量安装验收 18 项通过，实际旧密码复用成功，确认加载共享运行库并移除旧随包 DLL；安装目录 9183799 字节。完整模式新增保留打开方式的检查，最终 17 项待云端验证。

技术依据（2026-10-12）：[微软 .NET 分发说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/)说明共享运行库模式的依赖与体积取舍；[WPF 裁剪限制](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)明确当前 SDK 禁用 WPF 裁剪。沿用现有实现和微软 apphost，不引入未经支持的裁剪/AOT，也不以改变项目技术栈来隐瞒兼容成本。新报告为 `artifacts/light-installer-validation.json` 和 `artifacts/light-installed-smoke-validation.json`，普通安装/功能报告继续保留其各自范围。

第五公开预览 [v0.1.0-preview.5](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.5) 对应提交 `ddb3771c8f27e3fc212a3cf143ee1b79945744ef`，[Windows CI 38228863264](https://github.com/Nomoziya/nomozip/actions/runs/38228863264) 全部通过，包括原有功能、34 项桌面检查、17 项完整安装及 18 项完整到轻量安装；十二份报告和 34 张渲染图位于 `artifacts/github-validation-38228863264/validation-reports/`。云端轻量安装 9207798 字节，确认共享 .NET 10.0.12 和旧密码库实际复用。审核后的本地轻量载荷 9010036 字节 / 43 文件，完整载荷 155167983 字节 / 438 文件；两种载荷的 worker、26.04/26.03 引擎和产品固定公钥逐字节一致。轻量安装 EXE 2079477 字节，ZIP 3317169 字节；安装目录约 9 MB 不包含共享运行库。轻量 EXE/ZIP、完整 EXE/ZIP、86 文件源码快照及校验清单六项资产均通过公开地址无登录下载和 SHA-256 核验，稳定引擎渠道保持 `engine-26.04`；报告为 `artifacts/public-preview-5-validation.json`。已更新本机启动入口，新布局及完整人工验收继续待用户确认。
