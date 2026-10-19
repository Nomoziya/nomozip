# nomozip 0.3.2

本版增加任务控制、备份提醒、更新缓存清理和可选的7-Zip官方引擎源，沿用已采用的界面与统一样式。

- 解压任务可单独暂停、继续或取消；失败任务可重试或更换密码后重试。
- 密码库显示最近成功导出备份的时间和此后变更提醒。取消或失败的导出不计为成功备份；查看或使用密码不计为内容变更。
- 设置→更新可查看并清理历次更新留下的安装包缓存，暂时保留本会话当前尚待安装的准备包及占用中的文件。重启后不再处于准备状态的旧安装包可清理。
- 引擎更新来源可选7-Zip官方源，默认仍为nomozip已验证源。官方稳定版按架构与摘要校验，提取并自检成功后启用，支持重启校验与旧版回退，不执行下载的官方安装器。官方源仍通过GitHub下载。

提供Windows x64与原生ARM64的安装EXE、程序ZIP及通用源码ZIP。需要对应架构的.NET 10 Windows Desktop Runtime（10.0.12或更新10.0补丁版）；关闭软件后运行安装器升级，ZIP解压至新的空目录。升级与默认卸载保留密码库及设置，建议先导出加密备份。

## 验证与设计资料

功能候选来源f2ec68a，双架构[真实官方源及x64界面CI](https://github.com/Nomoziya/nomozip/actions/runs/39246197040)和[原生ARM64 CI](https://github.com/Nomoziya/nomozip/actions/runs/39247100549)通过。每架构官方源39项、签名源29项、新官方源UI中英各47项、产品界面中英各476项、自解压38项、软件更新中英各82项。上一功能候选的任务控制、重试、备份提醒及缓存清理证据继续有效，具体提交与验证范围见[当前摘要](CURRENT_STATE.md)。

0.3.2版本变更后的正式载荷、0.3.1升级/卸载和包完整性由发行隔离CI补检；此文件在发行源码冻结时不预先宣称其通过，最终结果以GitHub正式发行页与随附验收报告为准。UIA检查不等同物理鼠标操作，物理混合DPI、多屏、实际系统高对比切换和交互式讲述人未独立验收。

[统一UI规范](UI_STYLE_GUIDE.md)、[功能改进原图](../design-system/reference-20261017/task-improvements-20261019/index.html)与[官方源原图](../design-system/reference-20261017/official-engine-20261019/index.html)随源码保存，provenance.json记录逐文件来源与摘要。[0.3.0历史设计与43个原始候选](https://github.com/Nomoziya/nomozip/releases/tag/v0.3.0)及[0.3.1按钮修复资料](https://github.com/Nomoziya/nomozip/releases/tag/v0.3.1)继续保留。
