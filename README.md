# nomozip

Windows 桌面压缩包工具：使用官方 7-Zip 引擎，保存用户提供的解压密码，并在下次解压时自动尝试。

最新本地候选还支持 [修改已有 7z/ZIP](docs/ARCHIVE_EDITING.md)、[分卷创建](docs/SPLIT_VOLUMES.md)、[官方 7z 自解压包](docs/SELF_EXTRACTING.md)、[TAR、单文件 GZIP/XZ 创建](docs/CREATION_FORMATS.md)、[Windows ARM64 原生轻量包](docs/ARM64.md)、[简体中文/英语界面](docs/LOCALIZATION.md) 和 [跟随系统/浅色/深色主题](docs/APPEARANCE.md)。语言保存后重启，主题保存后立即生效且系统高对比度优先；用户密码和文件名保持原文。公开正式版的功能与下载入口见下文；开发候选未自动公开发布。

0.2.0 开发候选已加入 [密码库加密备份](docs/VAULT_BACKUP.md)、[基础右键菜单](docs/SHELL_MENU.md)、[软件正式版检查、下载安装及重启](docs/APPLICATION_UPDATES.md)、[解压规则、独立校验及结果目录操作](docs/EXTRACTION_TOOLS.md) 、[选择性解压](docs/SELECTIVE_EXTRACTION.md) 、[密码分组、来源及重复整理](docs/PASSWORD_ORGANIZATION.md) 和 [文件 SHA-256 与校验清单](docs/FILE_HASHES.md)、[ZIP 文件名编码](docs/ZIP_FILENAME_ENCODING.md)、[解压后的下载来源标记](docs/DOWNLOAD_ORIGIN.md)、[每显示器 DPI 与小窗口适配](docs/DISPLAY_SCALING.md)、[任务重排、单项解压、并发和暂停](docs/TASK_QUEUE.md)、[可选解压完成通知](docs/COMPLETION_NOTIFICATIONS.md)、[可选主密码与空闲锁定](docs/MASTER_PASSWORD.md)，仍仅提供轻量版。公开正式版保持 0.1.0，候选功能不表示已公开发布。

首个正式版 0.1.0 已公开，Windows x64，仅提供共享运行库轻量版。支持密码库、自动尝试和手动补充、7z/ZIP 创建及加密、7z/ZIP/RAR 内容浏览与解压，以及经本产品签名验证的官方引擎更新与回退。传统 ZIP、AES ZIP、RAR5 的同包多密码组合解压已专项验证；完整解压后才保存已验证的新密码。焦点框、任务文件朗读和极小工作区布局已修复，用户复验确认没有问题。正式版 [Windows CI](https://github.com/Nomoziya/nomozip/actions/runs/38270823087)、预览到正式版升级和四项公开发行资产 SHA-256 校验全部通过。

发布约定：用户于第七预览发布后要求后续只发布共享运行库轻量版。历史完整版本保持不变；未来安装 EXE 和 ZIP 均依赖与软件架构匹配的 .NET 10 Desktop Runtime，x64 与 ARM64 分包。

下载：[Windows x64 正式版 0.1.0](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0)。安装 EXE 约 2 MB，软件目录约 9 MB；依赖系统共享的 [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)（10.0.12 或更高的 10.0 补丁版），不需要 SDK。关闭软件后使用安装器升级，会保留密码库、设置和已下载的引擎。ZIP 请解压到新的空目录，运行 `ArchivePassword.exe`；约 9 MB 不包括共享运行库。历史完整独立版保持不变，不再发布新完整包。`engine-XX.XX` Release 仅用于引擎更新，不能单独当软件运行。

本机双击 [启动软件.cmd](启动软件.cmd)，或运行 [ArchivePassword.exe](artifacts/app/ArchivePassword.exe)。选择压缩包和输出目录后开始；在密码库中添加密码，或在手动输入时勾选成功后保存。

源码与 Windows 验证工作流位于 [Nomoziya/nomozip](https://github.com/Nomoziya/nomozip)。软件发行与稳定引擎更新分别发布；更新包使用独立产品发布密钥，客户端固定对应公钥。支持范围、自动检查与实际体验记录见[验收说明](docs/ACCEPTANCE.md)；实际不同 DPI 多屏切换尚未实测。

完整构建、运行与限制见 [开发与维护说明](docs/DEVELOPMENT.md)，更新与发布见 [更新说明](docs/UPDATES.md)。升级及默认卸载保留密码库，详见 [安装说明](docs/INSTALLATION.md)。

第四预览的压缩加密和自动解密、第五预览的解压布局和约 9 MB 体积已获用户确认；下一步请按 [第七预览人工验收](docs/INSTALLATION.md#第七预览的人工验收) 检查压缩包内容浏览、压缩页、添加记录后的密码库布局及其余实际桌面操作。

第三预览增加 7z/ZIP 创建、加密和可选 Windows 打开方式入口，[云端验证](https://github.com/Nomoziya/nomozip/actions/runs/38206962560) 全部通过。操作、密码限制与数据保护见 [压缩说明](docs/COMPRESSION.md)。第三预览当时尚未实现同包多密码组合；第九预览已补齐并通过本地专项检查。

第四预览修复高对比度配色、空密码焦点及小工作区窗口尺寸，[最终 Windows CI](https://github.com/Nomoziya/nomozip/actions/runs/38223276557) 全部通过。人工键盘、Narrator、实际 DPI/高对比度和 Explorer 操作仍待确认，范围见开发说明的 P5 章节。

第五预览根据实际反馈调整解压滚动布局：操作固定在底栏，普通宽窗口的设置卡片无需滚动，窄窗口保留溢出滚动并使用统一细轨。共享运行库轻量版保留全部现有功能，从已登记完整旧版升级会清理旧运行组件并保留密码/设置/入口和非产品文件。[最终 Windows CI](https://github.com/Nomoziya/nomozip/actions/runs/38228863264) 全部通过；完整到轻量安装已确认实际复用旧密码库并使用共享运行库。新布局仍需用户实际确认。

用户随后确认第五预览的解压布局和体积符合预期。第六预览把压缩页改为相同的工具栏、分栏和固定底栏结构，并修整密码库的行距、内容对齐、表头、启停/未使用状态和操作区域。[Windows CI 38236148792](https://github.com/Nomoziya/nomozip/actions/runs/38236148792) 全部通过；新增多条虚构记录和窄窗口检查，两页的新布局仍待用户体验。

第七预览补齐原计划中的只读内容浏览：选择压缩包后点击“查看内容”（Control L），搜索包内路径并查看类型、原始大小和加密标记。文件名加密时可复用密码库或手动输入；浏览不解压、不保存密码，也不标记任务已解压。[Windows CI 38239454113](https://github.com/Nomoziya/nomozip/actions/runs/38239454113) 全部通过，后续官方引擎的维护和签名门槛也已加入列表验证。

- [项目规划与阶段验收](PROJECT_PLAN.md)
- [当前支持范围、验证证据与交接](docs/ACCEPTANCE.md)
- [后续任务与相关产品研究](docs/ROADMAP.md)
- [轻量候选打包与维护](docs/RELEASE_MAINTENANCE.md)
- [官方引擎候选、签名与发布维护](docs/ENGINE_RELEASE_MAINTENANCE.md)
- [密码库加密备份与迁移（0.2.0 开发候选）](docs/VAULT_BACKUP.md)
- [右键解压与压缩（0.2.0 开发候选）](docs/SHELL_MENU.md)
- [界面设计规范与操作流程](design-system/MASTER.md)
- [解压主界面示意](design-system/extraction.svg)
- [界面预览图](design-system/extraction.png)

已确认在本目录新建项目，采用独立软件＋官方 7-Zip 引擎，自动尝试已保存密码。当前实现使用 C#、WPF、.NET 10 LTS，配合小型 C++ 引擎工作进程。

项目自己的版本、引擎版本和密码库数据版本分别管理。官方引擎新版本通过兼容性验收后发布到本产品更新渠道。


归档格式转换：普通 7z/ZIP/RAR → 新 7z/ZIP，原包保留，重新选择新包加密；逐文件 SHA-256 验证后交付。见 [转换指南](docs/ARCHIVE_CONVERSION.md)。

F22 已完成本地及完整双架构 CI 验收：转换数据 196、实际界面 80，完整报告 76 / 75 份通过。preview.23 两架构仅本地轻量候选，见 `artifacts/release-0.2.0-preview.23-final/` 和 `artifacts/release-0.2.0-preview.23-arm64-final/`；未发布或替换安装。其后的命令行验收见下段。

命令行与批处理入口已完成本地及完整双架构 CI 验收：独立 `nomozip-cli.exe` 提供解压、测试、创建和密码库编号查询，复用 GUI 业务/引擎；稳定 JSON/退出码、标准输入或遮罩输入密码、只读密码库引用。CLI 84 项、完整报告 77 / 76 份、安装升级各 26 项通过，见 [命令行说明](docs/COMMAND_LINE.md)。preview.24 两架构仅本地轻量候选：`artifacts/release-0.2.0-preview.24-final/` 与 `artifacts/release-0.2.0-preview.24-arm64-final/`；x64 载荷 11055321 字节 / 54 文件，比上一候选增加约 271 KiB，不增加用户运行组件安装。源码已上传，未发布或替换实际安装。之后的 F26 进展见下段。


包内文本/图片只读预览已完成本地与完整双架构 CI 验收：在“查看内容”中高亮一行，点击“预览文件”或 Ctrl+P。支持纯文本和 PNG/JPEG/BMP/GIF 首帧，读取后清理临时文件，密码库与解压任务状态不改写。数据 100 / WPF/UIA 174 项、完整 79 / 78 份报告通过，范围和限制见 [只读预览](docs/CONTENT_PREVIEW.md)。preview.25 两架构仅本地轻量候选：`artifacts/release-0.2.0-preview.25-final/` 与 `artifacts/release-0.2.0-preview.25-arm64-final/`；x64 11133997 字节 / 54 文件（+78676），无新增依赖或运行组件。源码已上传，未发布或替换实际安装。剩余 3 项开发子项及 F23 物理验收，下一项 F03 软件自动安装更新。


0.2.0-preview.26 完成 [软件下载安装与重启](docs/APPLICATION_UPDATES.md)：登记安装版可下载匹配架构的轻量安装包、校验并明确确认后升级；ZIP 保留下载页。最终产品源码 `11de81db88bee0638dde54133fe4c0ac88c3b512` 的 [x64 CI 38744221783](https://github.com/Nomoziya/nomozip/actions/runs/38744221783) / [原生 ARM64 CI 38744250096](https://github.com/Nomoziya/nomozip/actions/runs/38744250096) 全部通过，80 / 79 份报告与维护绑定复核通过；新增数据 55、中英文 UI 各 81、两架构隔离升级各 31 项。两种本地轻量候选未公开发布，不替换实际安装；x64 增加 37364 字节、无新增运行组件。剩余开发为 F04 拖出内容与 F07 跨重启续作，物理环境边界保留。
