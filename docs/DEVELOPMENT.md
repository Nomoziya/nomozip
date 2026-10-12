# 构建、验证与维护

当前交付为已公开发布的 0.1.0 开发预览，可运行的 Windows x64 WPF 软件。密码复用、本地签名更新与真实 GitHub 线上引擎更新闭环已验证；完整安装卸载、人工桌面验收和基础压缩功能仍在后续阶段。

第二预览增加当前用户安装包与携带运行组件的独立 ZIP。构建和安装/升级/卸载验收见 [安装说明](INSTALLATION.md)。普通开发构建继续使用系统运行时；`-SelfContained` 构建会携带 .NET / WPF 运行组件。安装器不会将这些组件安装为全局运行时。

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

第一条只检查官方稳定版；第二条在有新版本时下载官方包，禁用候选引擎，运行真实兼容性矩阵，通过后重新生成本地产品包。当前最新版 26.04 已在本地，因此本轮只验证了“当前版本无需更新”的分支；未来新版本的完整下载/验证/重建分支尚未实跑。

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
