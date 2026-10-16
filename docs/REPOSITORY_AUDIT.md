# 公开仓库隐私审查

2026-10-16。仓库按用户决定保持公开；本次不重写 Git 历史，也不覆盖旧发行资产。

## 范围与方法

审查基线为 `4f26929ec277d52fb5ea7b90d92a1525e8724b5c`：354 个当前跟踪文件、160 个提交、全部公开分支及标签可达的 1496 个文件对象。另检查 11 个公开发行的元数据、10 份源码 ZIP、公开 Issue 与提交评论（均为空），以及 preview.29 最终 x64 / ARM64 两次完整 CI 日志。

使用官方 Gitleaks 8.30.1，下载后核对发行方 SHA-256；Git 全历史与当前文件扫描启用完整脱敏，源码 ZIP 启用归档扫描。另检查个人目录、邮箱、手机号、凭据文件名与私钥标记。扫描原始报告仅保存在被忽略的本地 `artifacts/`；未读取生产私钥、本机登录令牌或实际密码库。此次未逐个逆向检查安装器二进制或遍历全部历史 CI 日志。

## 结果与处理

| 项目 | 结果 |
| --- | --- |
| API 令牌、私钥及真实密码库文件 | 在所述审查范围内未发现 |
| Gitleaks 疑似凭据 | 1 处，为 `tests/CheckpointValidation/Program.cs` 的明确虚构测试密码；用于构造测试归档，不访问外部服务，人工判定为误报，保留原始命中记录 |
| 私人邮箱 | 未发现；提交身份仅使用项目所有者的 GitHub 隐藏邮箱 |
| 手机号 | 未发现 |
| 个人路径 | 当前两份维护脚本和项目起点记录的三处路径已清理；脚本改用当前用户的环境目录解析，项目说明不再写具体本机位置 |
| 历史路径 | 旧提交及 10 份旧源码 ZIP 仍含旧本机路径。删除当前文件内容不会抹除历史；不能据此宣称历史隐私信息已全部移除 |
| 公开发行元数据与两份最终 CI 日志 | 未发现个人主目录；CI 日志的凭据扫描无命中 |
| 后续防护 | 补充忽略本机环境文件、密码库及凭据文件；源码打包继续使用显式允许列表，固定更新公钥保留 |

仓库所有者的公开 GitHub 用户名、仓库地址及第三方许可作者属于项目正常公开信息，未替换为虚构身份。历史路径只泄露开发账户/目录线索，本次未发现与之关联的真实密钥；没有执行凭据撤销、强制推送或删除旧附件。

证据：本地 `repository-privacy-inventory.json`、`repository-gitleaks-history.json`、`repository-gitleaks-current.json`、`repository-gitleaks-releases.json`、`privacy-source-archive-inventory.json`、`privacy-release-source-digests.json` 及两份 `privacy-final-*-ci-secrets.json`，均位于 `artifacts/`。

## 首页整理

参考 [NanaZip](https://github.com/M2Team/NanaZip)（审查时 15,846 星）和 [PeaZip](https://github.com/peazip/PeaZip)（7,971 星）的信息组织：简介、下载、功能、使用与文档入口。README 从 6574 字符减至约 1532 字符，移除逐版预览流水账、重复验收数字和本机开发入口；已有专题、项目记录与 Git 历史继续保留。后续遵循 [仓库维护约定](REPOSITORY_MAINTENANCE.md)，不将候选功能写成已发布功能。
