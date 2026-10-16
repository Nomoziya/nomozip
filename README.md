# nomozip

<img src="assets/nomozip-256.png" alt="nomozip 图标" width="80" height="80">

使用官方 7-Zip 引擎的 Windows 压缩工具。保存你提供的解压密码，下次解压时自动尝试。

[下载正式版](https://github.com/Nomoziya/nomozip/releases/tag/v0.2.1) · [使用文档](docs/README.md) · [更新记录](https://github.com/Nomoziya/nomozip/releases) · [反馈问题](https://github.com/Nomoziya/nomozip/issues)

## 下载与安装

当前正式版为 **0.2.1，Windows x64 / ARM64**。仅提供共享运行库轻量版：

| 架构 | 安装版 | 便携版 |
| --- | --- | --- |
| x64（多数 Intel / AMD 电脑） | [EXE](https://github.com/Nomoziya/nomozip/releases/download/v0.2.1/nomozip-0.2.1-light-setup-win-x64.exe) | [ZIP](https://github.com/Nomoziya/nomozip/releases/download/v0.2.1/nomozip-0.2.1-light-win-x64.zip) |
| ARM64（Windows on Arm） | [EXE](https://github.com/Nomoziya/nomozip/releases/download/v0.2.1/nomozip-0.2.1-light-setup-win-arm64.exe) | [ZIP](https://github.com/Nomoziya/nomozip/releases/download/v0.2.1/nomozip-0.2.1-light-win-arm64.zip) |

需要对应架构的 [.NET 10 Windows Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)（10.0.12 或更新的 10.0 补丁版），无需安装 SDK。安装包约 2–3 MB，程序目录约 8–12 MB，不包含系统共享运行库。

安装版用于安装或升级，保留密码库与设置；便携版解压到新目录，运行 `ArchivePassword.exe`。`engine-*` 发行仅供程序更新引擎，请下载带 `nomozip` 名称的软件包。

## 正式版功能

- **密码库**：自动尝试、分组与整理、独立加密备份，以及可选主密码和空闲锁定。
- **浏览与解压**：7z、ZIP、RAR/RAR5，包内预览与拖出、选择性解压、队列和可选跨重启续作。
- **压缩与校验**：加密压缩、分卷、自解压包、格式转换，以及文件 SHA-256 和校验清单。
- **更新与集成**：软件更新下载校验、官方引擎更新与回退、传统右键菜单和命令行入口。
- **界面**：简体中文/英语、浅色/深色/跟随系统主题，以及原生 ARM64 支持。

## 开始使用

1. 在“密码库”添加常用解压密码，也可以在需要时手动输入。
2. 在“解压”页添加压缩包、选择输出目录，然后开始解压。
3. 需要创建压缩包时，切换到“压缩”页，添加文件并选择格式。

只有完整解压成功后，才保存本次勾选保存的新密码。升级和默认卸载保留本机数据，详见[安装说明](docs/INSTALLATION.md)。

重启续作需先开启“保留解压进度”，以完整文件为检查点，不保存密码。升级或切换架构前，请先完成或丢弃保留任务，详见[续作说明](docs/EXTRACTION_CHECKPOINTS.md)。

## 文档与开发

- [使用文档与功能索引](docs/README.md)
- [0.2.1 发行说明](docs/RELEASE_0.2.1.md)
- [从源码构建](docs/DEVELOPMENT.md)
- [支持范围与验收记录](docs/ACCEPTANCE.md)
- [第三方组件及许可](THIRD_PARTY.md)

反馈问题时请提供软件版本、操作步骤与错误提示，并移除截图和日志中的真实密码、文件名及个人路径。
