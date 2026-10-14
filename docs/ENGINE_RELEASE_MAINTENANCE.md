# 引擎维护发布

当前 F13 扩展后的候选需通过九类矩阵：引擎、普通压缩、选择性解压、文件名编码、暂停、包修改、分卷、自解压、TAR/GZIP/XZ 创建。九份报告与官方 DLL/CLI/SFX/许可证/manifest、worker、公钥共 16 项文件哈希绑定；格式报告缺失、错误 DLL 或篡改均阻止签发。新增格式证据进入原有签名兼容性记录，更新 ZIP 五条目/schema 1 保留。下文旧矩阵数量是历史验收记录；本轮通过数量以项目计划最新记录为准。

F09 使用 `scripts/maintain-engine-release.ps1` 的同一入口，依次准备、签名、创建草稿和发布。准备和签名均在项目 `artifacts/` 的独立候选目录完成；不会重建主程序、覆盖系统 7-Zip 或读取用户密码库。发布确认针对已签名且已复核的具体草稿。此处的 RSA 是引擎真实性签名，与已移出验收的 Windows 代码签名不同。

## 准备候选

维护环境需要项目 SDK、当前 worker、26.04/26.03 基线、合成样本和 Python。已有开发环境可直接执行；新 checkout 按 [开发说明](DEVELOPMENT.md) 准备引擎、构建 worker 和生成样本。候选目录建议用短名称；Windows 原生输出仍有路径长度限制，测试中的深层夹具已改为短目录，未宣称实现任意长路径。

```powershell
# 默认取得官方最新稳定版；每轮使用尚不存在的目录。
./scripts/maintain-engine-release.ps1 -Mode Prepare -CandidateDirectory artifacts/e-next

# 指定官方稳定版，用不同目录保留证据。
./scripts/maintain-engine-release.ps1 -Mode Prepare -CandidateDirectory artifacts/e-2604 -Version 26.04

# 已准备的本地官方文件也可用，仍须完整重测该候选。
./scripts/maintain-engine-release.ps1 -Mode Prepare -CandidateDirectory artifacts/e-local -Version 26.04 -EngineDirectory engines/26.04
```

默认下载分支只接受 `ip7z/7zip` 稳定发布、对应版本的官方 x64 资产及 GitHub 提供的 SHA-256 摘要；下载字节须匹配摘要。指定本地文件时验证已有 manifest 的版本、架构、官方 URL 和 DLL 哈希，不将其标记为新下载的官方摘要验证。

候选先标记为禁用。随后使用隔离 worker 和样本运行五类真实矩阵：解压/浏览/组合密码、压缩、选择性解压、文件名编码、暂停。候选与固定基线重合时去重；当前两版的条目数分别为 101、38、86、75、31。失败保留日志和禁用状态，不生成可签名记录；全部通过后写入 `candidate-review.json`，绑定引擎文件、worker、固定公钥和五份报告的哈希。

## 本地签名

先审阅候选来源、矩阵与 `candidate-review.json`，再明确选择与产品固定公钥匹配的维护 RSA 私钥：

```powershell
./scripts/maintain-engine-release.ps1 -Mode Sign -CandidateDirectory artifacts/e-next -ProtectedPrivateKey '.tools/release-signing/private-key.dpapi'
# 或使用维护者自行保管的 PEM；两个参数只能选一个。
./scripts/maintain-engine-release.ps1 -Mode Sign -CandidateDirectory artifacts/e-next -PrivateKey '受控位置/发布私钥.pem'
```

签名前重新核对候选字节、当前公钥、worker 和配置仓库。签名工具自身再次检查绑定证据和目标版本覆盖，再加载私钥。生成的包须通过现有客户端验签器，`assets/` 仅包含 `ArchivePassword-engine-XX.XX-win-x64.zip` 和 `SHA256SUMS.txt`；包保留现有五条目的兼容格式，并在已签名的兼容性报告内附加选择、编码、暂停及 worker 证据。输出 `signed-review.json` 和草稿说明。

DPAPI 解密只针对显式指定的私钥，明文临时文件在 `finally` 中删除，解密字节清空；测试使用独立身份，不调用产品发布私钥。固定公钥不轮换，产品原有安装校验与回退保持不变。既有候选或签名资产不自动覆盖，失败后先检查证据，再选择新的目录重做。

## 草稿和确认发布

GitHub CLI 须已登录并有该仓库 Releases 写入权限。脚本使用 `config/update-channel.json` 中的仓库；测试使用本地模拟接口，不会访问真实发布渠道。提供已经审阅的完整 40 位源码提交：

```powershell
./scripts/maintain-engine-release.ps1 -Mode Draft -CandidateDirectory artifacts/e-next -TargetCommit '完整40位源码提交'

# 审阅 draft-review.json、具体草稿页面和下载资产后，明确批准发布：
./scripts/maintain-engine-release.ps1 -Mode Publish -CandidateDirectory artifacts/e-next -ApprovePublish
```

`Draft` 创建 `engine-XX.XX` 草稿，上传精确的两项资产，显式保持 `latest=false`。随后从 GitHub 重新下载，比对本地签名包和校验清单，保存 `draft-review.json`。已有同名 Release 会拒绝创建，不补传、不覆盖；部分远端操作失败后需要维护者查看草稿再处理。

`Publish` 缺少明确批准时不发出 GitHub 请求。批准后仍须重验本地证据、包签名、远端草稿状态、精确资产集合和下载字节；通过才将草稿设为稳定引擎 latest。发布后核对 `/releases/latest`，再次下载校验，写 `publish-review.json`。后验失败会报告失败，需要维护者检查远端状态；不会谎称完成或自动删除 Release。软件正式版继续显式 `latest=false`，不取代引擎渠道。

参数依据：[GitHub CLI 创建 Release](https://cli.github.com/manual/gh_release_create)、[编辑 Release](https://cli.github.com/manual/gh_release_edit)。

## 验证与范围

`tests/engine_release_validation.py` 在独立短路径下通过正式命令入口准备候选，实际跑五类矩阵，使用测试 RSA/DPAPI 身份生成更新包并由客户端验签。34 项覆盖篡改/失败阻断、临时私钥清理、原有程序/worker/公钥不变，以及模拟 GitHub 的草稿、重复标签、远端字节不一致、API 失败、批准门槛、稳定渠道和发布后复核。普通 Windows CI 加入该报告，密钥不会作为 CI 证据上传。

官方下载入口另用现有 26.04 在隔离目录实测；当前官方稳定版仍为 26.04，原公开资产保持不变。尚未遇到真实未来版本，未使用产品私钥生成新公开包，也未在生产 GitHub 上创建或发布草稿；模拟发布通过不等于这些生产操作已实测。将来实际新版本仍需完成同样的兼容性、签名和维护者审阅步骤。每日 CI 保持发现、验证和保存报告，不自动签发或发布生产更新。

完整 [Windows CI 38513508645](https://github.com/Nomoziya/nomozip/actions/runs/38513508645) 两个作业成功，53 份普通验收报告与此前 52 份加新维护报告的集合完全一致，逐项通过；新增维护 34/34，五类候选报告哈希与准备记录一致。证据：`artifacts/engine-release-validation.json`、`artifacts/engine-maintenance-ci-review.json`、`artifacts/engine-official-download-review.json`、`artifacts/engine-maintenance-local-review.json`。产品仍为 preview.14 的 9745096 字节 / 43 文件，旧候选资产保持原哈希；新维护源码快照在 `artifacts/engine-maintenance-final/`，一致性复核在 `artifacts/engine-maintenance-final-validation.json`。

2026-10-14 F10 扩展：后续候选必须额外通过 archive-editing-validation.json，矩阵由五类增为六类，绑定报告由五份增为六份（共 12 项文件哈希）。编辑证据同时要求目标版本覆盖及精确 DLL 哈希，维护包把编辑报告加入已签名兼容性记录，原有更新 ZIP 的五条目客户端格式保留。上文五矩阵/34 项是 F09 历史验证，扩展后的结果以最新计划记录为准。

F10 最终维护复验：Windows CI 38530974283 的 36/36 项通过，六类实际报告均与准备记录哈希一致，编辑矩阵 184 项；缺失/篡改与签发前复核保持有效。当前普通完整验收为 57 份报告，Windows 签名身份不在门槛内，未进行生产签名或发布。


2026-10-14 F11 扩展：候选新增 volume-validation.json，共七类矩阵和 13 项绑定哈希。分卷报告要求目标版本覆盖及精确 DLL 哈希，准备/复核/签发均检查，并加入已签名维护证据。历史六矩阵结果保留，当前完整验收以项目计划最新记录为准。


F11 最终维护复验：完整 Windows CI 38537947485 的 38/38 项通过，七类实际报告均与准备记录哈希一致，分卷矩阵 154 项。当前普通完整验收为 59 份报告，未进行生产签名或发布。


2026-10-14 F12 扩展：候选新增 sfx-validation.json 与官方同版 7z.sfx，共八类矩阵和 15 项文件绑定。模块/报告要求目标版本覆盖及精确 DLL/模块哈希。模块原样放入现有签名保护的兼容记录，原有 schema/五条目 ZIP/1 MiB 记录限制保留；旧客户端忽略新增记录字段。正常 push/每日发现只验证和保存证据，不访问生产私钥或自动发布。


F12 最终复验：完整 Windows CI 38544289926 的 43/43 项通过，八份候选报告与绑定记录一致，SFX 258 项含真实私有桌面执行，模块摘要与实际执行报告一致。当前普通完整验收为 61 份报告，没有生产签名或发布。
