# ARM64 原生发行

F14 当前处于完整验收阶段。原生 Windows ARM64 首轮引擎运行已通过；下面是已接入的行为，最终支持结论以项目计划中的完整 CI 和安装包复核为准。

软件保留 WPF/.NET 10 和独立 worker。x64 使用 MinGW，ARM64 使用 Visual Studio ARM64 C++ 工具链。ARM64 的程序启动器、worker、官方 DLL 和 CLI 都应为 AA64；打包和安装器构建检查 PE 与 manifest 架构，不能仅改文件名生成另一架构的包。两个架构分别发行共享运行库轻量包，不在一个包内携带所有架构。ARM64 需要匹配的 .NET 10 Windows Desktop Runtime ARM64。

官方 26.04/26.03 ARM64 文件来自 [7-Zip 官方发行](https://github.com/ip7z/7zip/releases)。同版官方 `7z.sfx` 仍为 x86 程序，保留原始字节，并通过 Windows 11 ARM64 的兼容运行验证独立解压；不将它标记为原生 ARM64，也不自改官方模块。

架构按正在运行的软件进程确定。x64 进程继续使用原有 `engines` 和 `active-engine.json`；ARM64 使用 `engines-arm64` 和 `active-engine-arm64.json`。密码库、独立备份和设置保留原来的用户目录，不因更换程序架构搬迁或清空。跨架构验收只使用同机同用户的合成 APV1/APV2 文件：实际 x64 进程创建，原生 ARM64 读取与保存，再由 x64 复读。跨电脑仍需独立加密导入导出，不能复制 DPAPI 文件替代该流程。

x64 引擎通道保留 `/releases/latest`。ARM64 从稳定发行列表分页查找对应架构资产，ARM64 的 `engine-XX.XX-arm64` 发行不覆盖 x64 latest。客户端拒绝错误架构的 manifest、官方 URL 或 DLL PE；安装后仍实际调用 worker 检查，再切换引擎，保留回退。

维护使用相同入口，新增实际架构矩阵，共十类报告、17 项绑定文件。只有目标原生进程及原生 OS 运行通过才可签发维护候选；离线签名检查支持明确指定目标架构，桌面安装默认只接受本进程架构。引擎 RSA 真实性签名保留；Windows 代码签名不属于本次验收。

```powershell
# 在原生 ARM64 Windows 开发/测试环境执行
./scripts/bootstrap.ps1 -Architecture arm64
./scripts/build-worker.ps1 -Architecture arm64
./scripts/bootstrap-installer.ps1 -Architecture arm64
./scripts/build.ps1 -Architecture arm64 -Configuration Release -SkipWorker -OutputDirectory artifacts/light-app
./scripts/maintain-engine-release.ps1 -Mode Prepare -Architecture arm64 -CandidateDirectory artifacts/arm-next
./scripts/build-release.ps1 -Architecture arm64 -ApplicationDirectory artifacts/light-app -OutputDirectory artifacts/arm-release
```

`test-arm64.ps1` 仅允许在一次性的 GitHub ARM64 Windows runner 上运行；其注册表与安装器操作留在该测试机。实际原生验收使用 [GitHub Windows ARM64 runner](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)，本机 x64 的 PE/包检查不替代 ARM64 的实际执行。工作流生成的预览资产不自动公开发布，也不替换用户实际安装。
