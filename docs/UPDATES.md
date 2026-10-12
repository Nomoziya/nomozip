# 引擎更新与发布

## 用户操作

在“设置”中查看现用引擎与上一版本。选择其它可用引擎后点击“使用所选版本”，选择会立即验证并保存；仅改变下拉选项不会改变运行引擎。任务运行期间不能安装或切换。

“检查更新”查询官方稳定版与产品 GitHub Releases；“下载并更新引擎”在产品提供较新版本时启用。也可以点击“导入更新包”，选择本产品发布的 `ArchivePassword-engine-XX.XX-win-x64.zip`。校验后显示版本并请求确认，验证引擎能解码产品的虚构样本后才启用。取消或失败保留当前引擎；启用后可以“恢复上一版本”。

此预览版更新仓库为 [Nomoziya/nomozip](https://github.com/Nomoziya/nomozip)，源码与 CI 已上传并通过云端验证。软件预览与稳定引擎包分别发布到 [Releases](https://github.com/Nomoziya/nomozip/releases)。设置中的地址可以更改，但文件仍必须由产品固定的发布公钥验证。普通官方安装程序不是本产品更新包。

产品发布签名的本地包位于 `artifacts/release-engine-packages/`，含 26.03 和 26.04。可以从 26.04 导入 26.03 验证明确降级确认和回退；无需等待官方发布一个新版本。旧 `artifacts/updates/` 中的开发签名包不再被新产品公钥接受；自动测试使用独立测试公钥与包。

## 保存位置与信任

- 随产品分发的引擎在程序目录 `engines/`；下载或导入的引擎在 `%LOCALAPPDATA%/ArchivePassword/engines/版本/`，不会覆盖系统 7-Zip。
- `active-engine.json` 是版本化的引擎选择记录，原子替换并保留副本；密码库单独存储。未知记录版本保持原文件，启用操作不会将它覆盖。
- ZIP 只允许固定文件名。`manifest.json` 的原始 UTF-8 字节由 RSA-PSS/SHA-256 验证；DLL、许可证和兼容性报告均按签名清单中的 SHA-256 校验。产品内固定公钥不从更新包或网络取得。
- 暂存目录通过后以目录移动方式安装，再独立验证和保存启用记录；安装成功但启用取消时，旧引擎保持现用。已安装的包在加载目录清单时重新校验，损坏项排除。

## 维护打包

先执行 `scripts/follow-official-engine.ps1 -Update` 或准备指定官方引擎，再运行兼容性矩阵。报告现在记录每个实测 DLL 的哈希；签名工具拒绝未通过、版本不对应或 DLL 已更改的报告。维护端兼容性矩阵覆盖广于客户端的微型解码检查。

从第七预览起，矩阵同时验证内容列表、大小/加密标记、文件名加密的密码请求和错密拒绝，覆盖 7z/ZIP/RAR/RAR5。签名工具要求目标引擎版本有对应列表检查，因此缺少列表覆盖的旧报告即使其它检查全通过也会被拒绝。报告结构和签名包载荷保持兼容；已有公开引擎资产不覆盖，未来候选按更新后的矩阵验收。

从第三预览起，维护脚本还要求候选引擎通过 7z/ZIP 创建与加密的压缩矩阵。签名打包同时读取 `artifacts/compression-validation.json`，验证各版本的实测 DLL 哈希和检查结果，并把压缩证据合并进受签名清单保护的兼容性报告；缺失或不对应的报告会阻止签名。已发布的旧引擎包保持不可变，两版既有官方 DLL 已通过新的压缩验证。

```powershell
# 当前维护电脑的发布私钥按 Windows 用户加密；脚本仅在签名时临时解密。
./scripts/package-engine-update.ps1 -Version 26.04 -ProtectedPrivateKey '.tools/release-signing/private-key.dpapi' -OutputDirectory 'artifacts/release-engine-packages'

# 正式维护密钥在受控位置，由维护者明确指定。
./scripts/package-engine-update.ps1 -Version 26.04 -PrivateKey '受控位置/发布私钥.pem'
```

默认输出目录为 `artifacts/updates/`，可通过 `-OutputDirectory` 指定；已存在的包不会自动覆盖。对应产品公钥在 `config/update-public-key.pem` 固定，打包时同时检查其与签名匹配。`config/update-channel.json` 的开发标识当前为 false，这仅表示使用产品发布密钥，软件本身仍是开发预览。

首次发布使用新生成的独立 RSA 3072 位密钥，私钥保存在被忽略的 `.tools/release-signing/private-key.dpapi`，由 DPAPI CurrentUser 保护；GitHub 和产品包仅含公钥及签名。公钥文件 SHA-256 为 `b86bbc5d17277454d63624e7691b8c677fe7b63f9ceaac47b18969e1fa3f0dac`。此保护依赖当前 Windows 用户，维护者应在更换电脑或重装前建立自己的受控密钥备份。轮换公钥需要产品更新，不能通过旧包自行更换信任公钥。

## GitHub 渠道约定

仓库的最新稳定 Release 中应包含 `ArchivePassword-engine-XX.XX-win-x64.zip`；草稿与预发布不用于客户端更新。包名中的版本与经签名验证的版本必须一致。稳定引擎包使用 `engine-XX.XX` 标签；软件开发预览使用 `v0.1.0-preview.1` 预发布标签，不将软件预览冒充稳定产品。发布者为用户已授权的 Nomoziya 账号。

客户端只发出公开仓库元数据和资产下载请求，不发送密码库、用户压缩包或解压记录。签名在下载后独立验证；GitHub 元数据中的版本名称不是真实性凭据。

CI 已部署每日检查官方版本、兼容性验证与报告保存，并通过首次真实 GitHub 运行。签名与发布没有被加入自动流程，当前由维护者在验证通过后执行；新版本发现、兼容性检查和报告自动化不等于无需维护者即可自动发布。

2026-10-12 已完成隔离维护演练：将现有官方 26.04 作为源码工作区中缺失的候选，原维护脚本实际完成下载、摘要核对、禁用候选、解压/压缩矩阵与重建；通过后才启用。另一个隔离副本模拟 worker 无法启动，验证失败时候选仍禁用，旧程序不被重建。成功副本的两类矩阵经过实际签名打包器生成测试更新包，验签通过，包内包含解压 84/84 与压缩 38/38 报告；26.04 与固定基线重合造成解压条目重复，只覆盖两种独立 DLL，不将其说成未来版本验证。

报告：`artifacts/upstream-maintenance-validation.json`、`artifacts/upstream-rejection-validation.json`、`artifacts/upstream-package-validation.json`。详细矩阵位于报告所指的 `artifacts/upstream-drill/` 副本。测试只使用独立临时签名身份，私钥已删除，不使用产品发布私钥、不改固定公钥、不发布包、不访问用户数据。主项目程序和公钥保持原哈希。真实未来新版本仍需要实测兼容性；原稳定 `engine-26.04` 资产继续保持不可变。

测试与打包使用独立的虚构样本。`scripts/test.ps1` 生成临时测试密钥、签名测试包和测试应用副本；这些数据不会进入源码包或用户软件包，也不要求正式密钥参与测试。

源码副本不包含当前开发预览的私钥，完整测试仍可运行。自行建立其它发布渠道时，需要生成自己的密钥对，明确更换固定公钥并重新构建软件；不能为原有软件伪造相同发布身份。

原始接口资料：[微软 RSA.VerifyData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.verifydata?view=net-10.0)、[GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)、[GitHub 定时工作流](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)、[MSYS2 构建环境](https://github.com/msys2/setup-msys2)。
