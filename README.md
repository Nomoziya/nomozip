# nomozip

Windows 桌面压缩包工具：使用官方 7-Zip 引擎，保存用户提供的解压密码，并在下次解压时自动尝试。

0.2.0 开发候选已加入 [密码库加密备份](docs/VAULT_BACKUP.md)、[基础右键菜单](docs/SHELL_MENU.md)、[软件正式版检查与下载页](docs/APPLICATION_UPDATES.md)、[解压规则、独立校验及结果目录操作](docs/EXTRACTION_TOOLS.md) 、[选择性解压](docs/SELECTIVE_EXTRACTION.md) 、[密码分组、来源及重复整理](docs/PASSWORD_ORGANIZATION.md) 和 [文件 SHA-256 与校验清单](docs/FILE_HASHES.md)、[ZIP 文件名编码](docs/ZIP_FILENAME_ENCODING.md)、[解压后的下载来源标记](docs/DOWNLOAD_ORIGIN.md)、[每显示器 DPI 与小窗口适配](docs/DISPLAY_SCALING.md)、[任务重排、单项解压、并发和暂停](docs/TASK_QUEUE.md)、[可选解压完成通知](docs/COMPLETION_NOTIFICATIONS.md)，仍仅提供轻量版。公开正式版保持 0.1.0，候选功能不表示已公开发布。

首个正式版 0.1.0 已公开，Windows x64，仅提供共享运行库轻量版。支持密码库、自动尝试和手动补充、7z/ZIP 创建及加密、7z/ZIP/RAR 内容浏览与解压，以及经本产品签名验证的官方引擎更新与回退。传统 ZIP、AES ZIP、RAR5 的同包多密码组合解压已专项验证；完整解压后才保存已验证的新密码。焦点框、任务文件朗读和极小工作区布局已修复，用户复验确认没有问题。正式版 [Windows CI](https://github.com/Nomoziya/nomozip/actions/runs/38270823087)、预览到正式版升级和四项公开发行资产 SHA-256 校验全部通过。

发布约定：用户于第七预览发布后要求后续只发布共享运行库轻量版。历史完整版本保持不变；未来安装 EXE 和 ZIP 均依赖 .NET 10 Desktop Runtime x64。

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
