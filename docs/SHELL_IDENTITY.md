# Windows 11 原生菜单部署审计

当前 0.2.0-preview.10 轻量载荷保持不变。传统菜单已交付，Windows 11 第一层原生菜单尚未公开启用。本次准备包身份部署和验收链路，不修改 WPF 技术栈或更换安装器。

## 方案与验证

借鉴微软 sparse package 与 Notepad++ 的部署流程，复用现有三种操作和原生 IExplorerCommand；自建小型构建/验收脚本。Windows SDK MakeAppx 实际校验发现旧原型清单的 COM 顺序和 EntryPoint 冲突，已修复。SDK BuildTools 固定 10.0.26100.7705，由官方 NuGet 取得，并验证该版 catalog 的 SHA-512；工具只供开发，不随产品发行。

- `scripts/build-shell-identity-prototype.ps1`：输出原生 DLL、仅元数据的 `.msix`、构建数据和 SDK 日志；不注册身份、不签名或导入证书。本地修正后的 `artifacts/shell-identity-folder-fix/` 不带测试入口，身份包 2111 字节，DLL 173568 字节。objdump 依赖检查不含 libwinpthread/libstdc++/libgcc 运行 DLL；不增加运行库。
- `scripts/test-shell-identity.ps1`：仅临时 GitHub 托管 Windows VM 可执行。独立复制 WPF 载荷并嵌入匹配身份，建立随机命名的测试证书与包；签名后由 Windows 注册并从已获身份的原生客户端通过系统 COM 激活菜单，自动启动现有 WPF 业务，检查多选及真实解压输出。继续验证身份升级、移除和外部程序保留，退出清理自己的包和证书。不直接加载 DLL 代替系统激活，不登记真实用户默认应用，不重启 Explorer。
- 当前本地只构建未签名原型及核实禁止本机部署的入口，未修改本机证书信任、开发者模式、已安装软件或实际菜单。最终 CI 已通过，真实 Windows 11 第一层菜单显示仍未测试；Windows Server 上的包注册/COM 成功也不能代替此体验。

## 公开发行待定事项

终端分发的身份包需要受 Windows 信任的签名证书，Publisher 必须匹配证书 Subject。现有引擎更新签名用于产品内部验证，不具备 Windows 代码签名信任。自签证书只能在用户明确选择信任后使用；不默认将测试信任带入产品安装器。

官方 Windows 11 `-AllowUnsigned` 路线明确供快速测试，带可执行内容通常还需管理员权限；不选作公开安装路径。证书或受信任签名服务的使用涉及额外材料或成本，需要先确定发行方式，再接入生产登记/卸载和 Windows 11 体验验收。

## 原始来源（2026-10-13）

- [微软身份包与现有安装器](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)：manifest、融合身份、外部位置、签名和安装移除。
- [微软 File Explorer 原生菜单](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)：IExplorerCommand、COM、菜单上下文和实际显示验证。
- [微软无签名 MSIX 的测试范围](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)：测试用途及权限。
- [Notepad++ nppShell](https://github.com/notepad-plus-plus/nppShell/blob/master/README.md)：小型 DLL 与身份包、签名及安装器接入的实践；只借鉴流程，未复制其代码。
- [微软 winapp CLI sparse 指南](https://github.com/microsoft/winappCli/blob/main/docs/guides/sparse.md)：保留 NSIS 安装路径的参考，未将该 CLI 添加为产品依赖。
- [固定 SDK 官方 NuGet 记录](https://api.nuget.org/v3/registration5-gz-semver2/microsoft.windows.sdk.buildtools/10.0.26100.7705.json)：指向原始 catalog 的版本和包校验数据。


部署取证中遇到的资料差异：身份包示例建议 CurrentUser/TrustedPeople，但实际 VM 的 Add-AppxPackage 报 0x800B0109；MSIX 故障文档要求计算机存储。隔离脚本现只将自己的测试公钥导入 VM 的 LocalMachine/TrustedPeople，并检查包、信任证书和私钥存储清理，不加入 Root。来源：[微软 MSIX 故障指南](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide)。

可选公开签名路径：已有受信任证书可复用；[SignPath Foundation 免费开源方案](https://signpath.org/terms)需要 OSI 许可、项目信誉/审核和发布签名批准，不能保证受理。当前项目未配置自有代码许可，选择此路还需决定许可。商业证书/签名服务涉及费用和主体/地区资格，未购买、申请或上传材料；不承诺某个服务必然可用。


系统激活取证：机器 Trusted People 配置后包注册成功，0x8007007E 出现在原生 COM 激活。PE 检查发现旧原型依赖 libwinpthread-1.dll；开发工具 PATH 掩盖了该依赖。现使用完整静态链接并记录导入库，原生客户端仅使用 Windows 系统 PATH 检查系统激活，避免再次借助开发环境。旧直接 LoadLibrary 原型测试也使用同样构建设置。


菜单状态取证：系统 COM 根对象激活成功后，解压项却隐藏。本机真实 Shell 元数据复现 7z 的 SFGAO 属性 0x60000000（文件系统 + Shell 文件夹），文件系统属性为 0x20（普通归档文件）。改用物理目录属性判断。17 项检查已在本机通过，覆盖 7z/ZIP/RAR、混合归档、普通文件、带 .7z 后缀的真实目录、混合不支持项及压缩选择；不登记或执行菜单。源码：[微软 SFGAO 说明](https://learn.microsoft.com/zh-cn/windows/win32/shell/sfgao)。完整系统激活复验已通过。


## 验收与用户决定

最终源码 `fc43d5373e375a4f3cd65d955e3743339314ac5e` 的 [Windows CI 38431690497](https://github.com/Nomoziya/nomozip/actions/runs/38431690497) 两个作业成功，46 份报告下载至 `artifacts/github-validation-38431690497/`，与原 44 份加新增两份逐项核对全部通过。新增真实包签名/注册、身份客户端/系统 COM、自动 WPF 处理、两种解压真实输出、压缩确认、身份升级/移除、外部程序保留和自己的包/证书清理 18 项；真实 Shell 元数据菜单状态 17 项，本地也通过。现有引擎 101、混合密码 48、压缩 38、DPI 97、MOTW 96/20/36、编码 75/31、密码整理 30/30、SHA-256 34/43、选择 86/31、备份及跨用户、软件更新、菜单、安装升级和布局/焦点等有效结果全部保持通过。

用户已选择暂缓原生菜单公开启用，保留传统菜单，继续 B4 队列与发行维护。不申请或购买签名，不更改产品许可，不向本机导入测试证书。当前只有 Windows Server 2025 临时 VM 的包部署/系统调用证明；真实 Windows 11 第一层菜单显示与正式生产安装器接入未验收，不能记为原生菜单已交付。公开正式版 0.1.0 与本地轻量候选 0.2.0-preview.10 的载荷保持不变（9590705 字节 / 43 文件）。
