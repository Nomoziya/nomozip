# nomozip

<img src="assets/nomozip-256.png" alt="nomozip 图标" width="80" height="80">

使用官方 7-Zip 引擎的 Windows 压缩工具。保存你提供的解压密码，下次解压时自动尝试。

[下载正式版](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0) · [使用文档](docs/README.md) · [更新记录](https://github.com/Nomoziya/nomozip/releases) · [反馈问题](https://github.com/Nomoziya/nomozip/issues)

## 下载与安装

当前正式版为 **0.1.0，Windows x64**。仅提供共享运行库轻量版：

| 文件 | 用途 |
| --- | --- |
| [安装版 EXE](https://github.com/Nomoziya/nomozip/releases/download/v0.1.0/nomozip-0.1.0-light-setup-win-x64.exe) | 安装或升级，保留密码库与设置 |
| [便携版 ZIP](https://github.com/Nomoziya/nomozip/releases/download/v0.1.0/nomozip-0.1.0-light-win-x64.zip) | 解压到新目录，运行 `ArchivePassword.exe` |

需要 [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)（10.0.12 或更新的 10.0 补丁版），无需安装 SDK。正式版安装包约 2 MB，程序目录约 9 MB，不包含系统共享运行库。

**0.2.0 正在准备发布**；开发候选支持的功能见[后续规划](docs/ROADMAP.md)，不代表上述正式版已支持。`engine-*` 发行仅供程序更新引擎，请下载带 `nomozip` 名称的软件安装包。

## 正式版功能

- **密码库**：添加、编辑、搜索常用密码，解压时自动尝试；缺少密码时可手动输入。
- **浏览与解压**：查看 7z、ZIP、RAR/RAR5 内容，支持加密压缩包。
- **压缩与加密**：创建 7z、ZIP，选择压缩级别和密码；7z 支持加密文件名。
- **引擎维护**：检查官方引擎更新，校验产品签发的更新包，并支持回退。
- **Windows 集成**：可添加“打开方式”入口，默认应用由用户在 Windows 中选择。

## 开始使用

1. 在“密码库”添加常用解压密码，也可以在需要时手动输入。
2. 在“解压”页添加压缩包、选择输出目录，然后开始解压。
3. 需要创建压缩包时，切换到“压缩”页，添加文件并选择格式。

只有完整解压成功后，才保存本次勾选保存的新密码。升级和默认卸载保留本机数据，详见[安装说明](docs/INSTALLATION.md)。

## 文档与开发

- [使用文档与功能索引](docs/README.md)
- [从源码构建](docs/DEVELOPMENT.md)
- [支持范围与验收记录](docs/ACCEPTANCE.md)
- [第三方组件及许可](THIRD_PARTY.md)

反馈问题时请提供软件版本、操作步骤与错误提示，并移除截图和日志中的真实密码、文件名及个人路径。
