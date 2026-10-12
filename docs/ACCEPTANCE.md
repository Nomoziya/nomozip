# 验收与交接

2026-10-12 后续范围已确认：暂不增加功能，以第九预览完成实际系统体验与正式版准备。按用户要求，先提供[具体操作清单](SYSTEM_ACCEPTANCE.md)，正式版等待用户测试并反馈后再发布；当前人工项目仍未确认，既有通过结果保留。

更新：2026-10-12，Asia/Shanghai。当前交付为 [第九预览](https://github.com/Nomoziya/nomozip/releases/tag/v0.1.0-preview.9)，产品代码 `7bea5ae6ebc0aeb0ce78e2553bc590272b8f9eae`，发布标签对应 `8a86c4062d4567daef7dcdc8cc0c518abb7cd1aa`。本次交接只修改说明，保留已经通过的产品与验证结果。

## 已交付范围

| 需求 | 实现与边界 |
| --- | --- |
| 保存和复用密码 | 当前 Windows 用户的 DPAPI 加密密码库；添加、编辑、搜索、删除、自动尝试和手动补充。完整解压成功后才保存新密码和记录成功。 |
| 浏览和解压 | 7z、ZIP、RAR/RAR5 的真实样本；只读内容浏览与搜索；错误详情、取消、同名文件处理。混合密码专项覆盖传统 ZIP、AES ZIP 和 RAR5；其它混合密码变体未专项验证。 |
| 压缩 | 创建 7z/ZIP，压缩级别、加密、7z 文件名加密；新包校验后保存或替换。ZIP 创建密码当前仅支持 ASCII；不创建 RAR。 |
| 跟进官方引擎 | 官方候选下载、兼容性矩阵、签名更新包、启用与回退；每日 CI 检查。签名和发布由维护者执行；实际未来版本须逐版通过矩阵。 |
| 清晰一致的界面 | 解压和压缩采用统一布局，窄窗口可达；密码库紧凑行距、对齐和状态文本；沿用 WPF 与既有设计规范。 |
| 轻量分发 | 仅发布共享 .NET 运行库轻量 EXE/ZIP。第九预览载荷 9085627 字节 / 43 文件，云端安装 9288888 字节；系统 .NET 10 Desktop Runtime x64 空间另计。 |
| 后续扩展 | Desktop、Core、Infrastructure、原生 worker 分离；版本化密码库/设置及独立引擎选择。加密导入导出、标签、规则、ARM64、多语言等仍为待确认候选。 |

## 已有有效证据

[最终 Windows CI 38257418706](https://github.com/Nomoziya/nomozip/actions/runs/38257418706) 全部通过；下载报告位于 `artifacts/github-validation-38257418706/validation-reports/`，共 15 份 JSON、36 张虚构数据渲染。各数字仅表示其对应检查范围，不合并为人工验收数量。

| 检查 | 结果 | 报告 |
| --- | --- | --- |
| 两版引擎、列表、实际解压及错误/取消 | 101/101 | `engine-validation.json` |
| 组合密码、真实输出、保存与取消 | 48/48 | `multi-password-validation.json` |
| 正式 WPF 组合密码命令流程 | 14/14 | `desktop-multi-password-validation.json` |
| 密码库、候选及普通解压 | 25/25 | `core-validation.json` |
| 压缩与官方 CLI 逐字节比较 | 38/38；每版含四次快速取消 | `compression-validation.json` |
| 引擎更新、签名拒绝与回退 | 22/22 | `update-validation.json` |
| 只读浏览及其桌面入口 | 28/28、13/13 | `browser-validation.json`、`desktop-browser-validation.json` |
| 失败详情及其桌面入口 | 10/10 | `desktop-failure-validation.json` |
| 布局、UIA 名称、逻辑焦点和模拟渲染 | 44/44 | `accessibility-validation.json` |
| 打开方式登记与保留旧默认 | 7/7 | `association-validation.json` |
| 轻量安装、旧完整到轻量升级与卸载 | 18/18 | `light-installer-validation.json` |
| 缺失/不匹配维护证据拒签 | 通过 | `signing-validation.json` |

四项公开发行资产经过无登录下载，与本地和 GitHub SHA-256 一致，见 `artifacts/public-preview-9-validation.json`；包 CRC、私密材料排除、固定公钥及仅轻量发布检查见 `artifacts/preview-9-package-validation.json`。已有真实线上引擎更新的 8 项检查与隔离官方维护演练保留原验证范围，见 [更新说明](UPDATES.md)。

## 用户已确认与尚未确认的范围

第四预览的压缩加密与自动解密、第五预览的解压布局及约 9 MB 体积、第七预览的压缩页/密码库布局与内容浏览已获用户确认。第八预览用户明确未测试；第九预览由代理完成上述检查，未记录为人工通过。

按照用户确认的职责，新增功能由代理测试和改进，不要求逐版重复人工验收。真实键盘/讲述人、操作系统高对比度、物理多屏 DPI 与 Explorer 的实际使用仍未取得体验确认；UIA、逻辑焦点、资源/密度模拟和登记测试各自保留真实范围。用户实际安装中的旧记录是否保留也尚无确认，隔离升级已经验证保留及复用。

这些实际环境状态不改写为通过，也不作为每个新增功能重复等待用户测试的关卡。实际遇到问题时，按“操作步骤、实际结果、预期结果”修复受影响部分；未改变的有效结果继续保留。

## 维护入口

- 构建与本地检查：[开发说明](DEVELOPMENT.md)。
- 官方候选、签名、稳定渠道和恢复：[更新说明](UPDATES.md)。
- 轻量构建、安装升级、卸载及实际环境操作：[安装说明](INSTALLATION.md)。
- 当前范围、候选需求与阶段状态：[项目规划](../PROJECT_PLAN.md)。

当前确认范围的功能实现、代理验收和维护交接已完成。软件仍为开发预览；真实环境的未验证状态保留，正式版本不在本轮发布。新需求先确定范围，再实施及验证；本轮不新增候选功能、不重测未改变的产品、不覆盖历史资产。
