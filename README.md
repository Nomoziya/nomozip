# nomozip

Windows 桌面压缩包工具：使用官方 7-Zip 引擎，保存用户提供的解压密码，并在下次解压时自动尝试。

当前状态：0.1.0 Windows x64 开发预览已构建。引擎 57 项、核心 17 项、更新 22 项，以及 3 条桌面流程验证通过；真实线上引擎更新的 8 项桌面检查通过，完整用户验收仍待完成。

下载：[Windows x64 开发预览](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.1)。解压后运行 `ArchivePassword.exe`；需要 [.NET 10 Windows Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。稳定引擎包发布在独立的 `engine-XX.XX` Release，不能单独当软件运行。

本机双击 [启动软件.cmd](启动软件.cmd)，或运行 [ArchivePassword.exe](artifacts/app/ArchivePassword.exe)。选择压缩包和输出目录后开始；在密码库中添加密码，或在手动输入时勾选成功后保存。

当前预览支持加密密码库、自动尝试密码、更新包导入、GitHub Releases 更新下载、明确启用与恢复上一引擎。源码与 Windows 验证工作流已上传到 [Nomoziya/nomozip](https://github.com/Nomoziya/nomozip)，首次云端验证通过。软件下载与稳定引擎更新分别发布，软件仍为开发预览；下载入口见 [Releases](https://github.com/Nomoziya/nomozip/releases)。更新包使用独立产品发布密钥，客户端固定对应公钥。

完整构建、运行与限制见 [开发与维护说明](docs/DEVELOPMENT.md)，更新与发布见 [更新说明](docs/UPDATES.md)。同包多密码组合解压、基础压缩功能及安装包仍待完成。

第二预览安装包与携带运行组件的 ZIP 已进入发布准备；无需另装 .NET，升级及默认卸载保留密码库，详见 [安装说明](docs/INSTALLATION.md)。同包多密码组合解压与基础压缩仍属于后续阶段。

- [项目规划与阶段验收](PROJECT_PLAN.md)
- [界面设计规范与操作流程](design-system/MASTER.md)
- [解压主界面示意](design-system/extraction.svg)
- [界面预览图](design-system/extraction.png)

已确认在本目录新建项目，采用独立软件＋官方 7-Zip 引擎，自动尝试已保存密码。当前实现使用 C#、WPF、.NET 10 LTS，配合小型 C++ 引擎工作进程。

项目自己的版本、引擎版本和密码库数据版本分别管理。官方引擎新版本通过兼容性验收后发布到本产品更新渠道。
