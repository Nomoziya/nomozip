# Windows 11 原生菜单部署审计

当前 0.2.0-preview.10 轻量载荷保持不变。传统菜单已交付，Windows 11 第一层原生菜单尚未公开启用。本次准备包身份部署和验收链路，不修改 WPF 技术栈或更换安装器。

## 方案与验证

借鉴微软 sparse package 与 Notepad++ 的部署流程，复用现有三种操作和原生 IExplorerCommand；自建小型构建/验收脚本。Windows SDK MakeAppx 实际校验发现旧原型清单的 COM 顺序和 EntryPoint 冲突，已修复。SDK BuildTools 固定 10.0.26100.7705，由官方 NuGet 取得，并验证该版 catalog 的 SHA-512；工具只供开发，不随产品发行。

- `scripts/build-shell-identity-prototype.ps1`：输出原生 DLL、仅元数据的 `.msix`、构建数据和 SDK 日志；不注册身份、不签名或导入证书。本地 `artifacts/shell-identity-audit-r4/` 含带专用测试入口的包，2133 字节，DLL 169984 字节；约 168 KiB，不增加运行库。
- `scripts/test-shell-identity.ps1`：仅临时 GitHub 托管 Windows VM 可执行。独立复制 WPF 载荷并嵌入匹配身份，建立随机命名的测试证书与包；签名后由 Windows 注册并从已获身份的原生客户端通过系统 COM 激活菜单，自动启动现有 WPF 业务，检查多选及真实解压输出。继续验证身份升级、移除和外部程序保留，退出清理自己的包和证书。不直接加载 DLL 代替系统激活，不登记真实用户默认应用，不重启 Explorer。
- 当前本地只构建未签名原型及核实禁止本机部署的入口，未修改本机证书信任、开发者模式、已安装软件或实际菜单。CI 尚待验收，真实 Windows 11 第一层菜单显示仍未测试；Windows Server 上的包注册/COM 成功也不能代替此体验。

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
