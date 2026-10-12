# 引擎更新与发布

## 用户操作

在“设置”中查看现用引擎与上一版本。选择其它可用引擎后点击“使用所选版本”，选择会立即验证并保存；仅改变下拉选项不会改变运行引擎。任务运行期间不能安装或切换。

“检查更新”查询官方稳定版与产品 GitHub Releases；“下载并更新引擎”在产品提供较新版本时启用。也可以点击“导入更新包”，选择本产品发布的 `ArchivePassword-engine-XX.XX-win-x64.zip`。校验后显示版本并请求确认，验证引擎能解码产品的虚构样本后才启用。取消或失败保留当前引擎；启用后可以“恢复上一版本”。

此预览版更新仓库为 [Nomoziya/nomozip](https://github.com/Nomoziya/nomozip)，公开仓库已创建并验证；源码和产品 Release 尚未上传。设置中的地址可以更改，但文件仍必须由产品固定的发布公钥验证。普通官方安装程序不是本产品更新包。

本地演示包位于 `artifacts/updates/`，含 26.03 和 26.04。它们使用开发预览签名，只用于当前预览验收。可以从 26.04 导入 26.03 验证明确降级确认和回退；无需等待官方发布一个新版本。

## 保存位置与信任

- 随产品分发的引擎在程序目录 `engines/`；下载或导入的引擎在 `%LOCALAPPDATA%/ArchivePassword/engines/版本/`，不会覆盖系统 7-Zip。
- `active-engine.json` 是版本化的引擎选择记录，原子替换并保留副本；密码库单独存储。未知记录版本保持原文件，启用操作不会将它覆盖。
- ZIP 只允许固定文件名。`manifest.json` 的原始 UTF-8 字节由 RSA-PSS/SHA-256 验证；DLL、许可证和兼容性报告均按签名清单中的 SHA-256 校验。产品内固定公钥不从更新包或网络取得。
- 暂存目录通过后以目录移动方式安装，再独立验证和保存启用记录；安装成功但启用取消时，旧引擎保持现用。已安装的包在加载目录清单时重新校验，损坏项排除。

## 维护打包

先执行 `scripts/follow-official-engine.ps1 -Update` 或准备指定官方引擎，再运行兼容性矩阵。报告现在记录每个实测 DLL 的哈希；签名工具拒绝未通过、版本不对应或 DLL 已更改的报告。维护端兼容性矩阵覆盖广于客户端的微型解码检查。

```powershell
# 本地开发预览，私钥仅留在被忽略的 .tools/update-signing/ 目录。
./scripts/package-engine-update.ps1 -Version 26.04 -Development

# 正式维护密钥在受控位置，由维护者明确指定。
./scripts/package-engine-update.ps1 -Version 26.04 -PrivateKey '受控位置/发布私钥.pem'
```

输出目录为 `artifacts/updates/`，已存在的包不会自动覆盖。对应产品公钥须在 `config/update-public-key.pem` 固定，打包时同时检查其与签名匹配。`config/update-channel.json` 的开发标识当前为 true。

首次正式发布前，生成并保管正式发布密钥，将正式公钥固定到新产品构建并调整渠道标识；不将当前开发私钥用作正式渠道，也不上传任何私钥。轮换公钥需要产品更新，不能通过旧包自行更换信任公钥。本轮未创建正式密钥、发布 Release 或上传源码。

## GitHub 渠道约定

仓库的最新稳定 Release 中应包含 `ArchivePassword-engine-XX.XX-win-x64.zip`；草稿与预发布不用于客户端更新。包名中的版本与经签名验证的版本必须一致。首次发布需要仓库已创建，并确认发布身份和待发布内容。

客户端只发出公开仓库元数据和资产下载请求，不发送密码库、用户压缩包或解压记录。签名在下载后独立验证；GitHub 元数据中的版本名称不是真实性凭据。

CI 已准备每日检查官方版本、兼容性验证与报告保存。正式签名与发布没有被加入自动流程；CI 尚未部署，不能把本地测试当作 GitHub 运行成功。

测试与打包使用独立的虚构样本。`scripts/test.ps1` 生成临时测试密钥、签名测试包和测试应用副本；这些数据不会进入源码包或用户软件包，也不要求正式密钥参与测试。

源码副本不包含当前开发预览的私钥，完整测试仍可运行。自行建立其它发布渠道时，需要生成自己的密钥对，明确更换固定公钥并重新构建软件；不能为原有软件伪造相同发布身份。

原始接口资料：[微软 RSA.VerifyData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.verifydata?view=net-10.0)、[GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)、[GitHub 定时工作流](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)、[MSYS2 构建环境](https://github.com/msys2/setup-msys2)。
