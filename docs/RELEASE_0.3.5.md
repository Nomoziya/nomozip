# nomozip 0.3.5

本版保留0.3.4的浏览、压缩、预览与UI改进，补齐“定位所在目录”菜单项缺少的图标，使其与其它菜单及主题保持一致。

0.3.4的双架构发行、正式0.3.3升级和公开下载均已通过，但发布后的完整页面回归发现该菜单图标遗漏。0.3.5已补验完整双语产品页面，并通过从正式0.3.4升级、安装器及包完整性检查；0.3.4原资产保留。详细功能见[0.3.4发行说明](https://github.com/Nomoziya/nomozip/releases/tag/v0.3.4)。

公开附件只有Windows x64 / 原生ARM64安装EXE及SHA256SUMS.txt。需要对应架构的.NET 10 Windows Desktop Runtime（10.0.12或更新的10.0补丁版），无需SDK。关闭软件后运行对应安装器升级；升级和默认卸载保留密码库与设置，建议先导出加密备份。软件与引擎发行渠道保持原设置。

[x64完整UI CI39736478066](https://github.com/Nomoziya/nomozip/actions/runs/39736478066)、[原生ARM64完整UI CI39736544094](https://github.com/Nomoziya/nomozip/actions/runs/39736544094)均成功：中英页面各477项、按钮各180项，11个菜单图标均符合主题。标签6e06a9d与通过UI的a29381a代码树完全一致。[正式发行CI39737126466](https://github.com/Nomoziya/nomozip/actions/runs/39737126466)双架构成功：各16份报告、正式0.3.4升级/首次重启清理/卸载37项、包完整性8项；主程序/CLI为0.3.5.0及同一完整源码后缀，两个源码包的4237个文件内容一致。完整范围见[当前摘要](https://github.com/Nomoziya/nomozip/blob/main/docs/CURRENT_STATE.md)。物理多屏、真实系统高对比切换和交互读屏未冒称执行。


三个正式附件已匿名回下载，与本地及GitHub服务器摘要逐字节/SHA256一致。现有软件更新检查为x64/ARM64正确识别0.3.5，引擎latest保持engine-26.04。
