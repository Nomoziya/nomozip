# 每显示器 DPI 与小工作区

0.2.0-preview.10 开发候选采用 PerMonitorV2，沿用 WPF、现有字体和设计规范，不增加运行组件。公开正式版 0.1.0 不因此改变。

Windows 与 WPF 负责按窗口所在屏幕的 DPI 渲染；程序只根据当前显示器工作区调整窗口大小和位置。对话框优先围绕所有者居中，屏幕负坐标按原生像素处理。拖动跨屏期间不会将窗口夹回旧显示器；移动结束、DPI 或工作区变化后重新适配。最大化和最小化保留 Windows 原生行为。

主窗口与密码、密码编辑、备份、导入、解压规则、内容、失败详情、SHA-256、编码和结果目录十类对话框共用此行为。常规尺寸保留既有布局，极小工作区下内容和编码窗口收起重复说明；SHA-256 将所选文件的校验区变为可展开区域，放大后恢复完整布局，已输入值继续保留。

## 验证范围

`--validate-dpi` 使用独立合成数据和不可见测试窗口，核实 EXE 内嵌清单、真实 HWND 的 PMv2 上下文、WPF 与原生 DPI 一致、工作区边界、主窗四页和十类对话框操作可达。另验证 512×360 DIP 下列表整行和校验输入、正常布局恢复，以及 100%/125%/150%/200%/250% 与负屏幕坐标的几何计算。报告为 `artifacts/dpi-ui-validation.json`，截图为 `artifacts/dpi-preview/`。

本机实际测得 144 DPI（150%），只枚举一个活动显示器。几何/紧凑布局模拟不能证明真实不同 DPI 屏幕间移动、系统窗口重排或文字清晰度已通过；这些保持未实测。测试不改变用户的系统缩放、默认应用或真实密码库，不替换已安装程序。

具备两块不同缩放显示器时，再从正常入口检查：在两屏之间缓慢拖动主窗并来回切换解压/压缩/密码库/设置；在副屏打开密码、内容和 SHA-256 窗口；切换最大化/还原并用 Tab 检查焦点；确认文字清晰、窗口留在当前屏幕工作区、任务栏不遮挡底部操作。记录显示器分辨率/缩放和实际结果，才能补齐物理跨屏验收。

## 原始依据

- [Windows 默认 DPI 感知清单](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)：使用 manifest 声明 PMv2，保留旧感知回退。
- [WPF OnDpiChanged](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.ondpichanged?view=windowsdesktop-10.0)：由框架接收并更新 DPI。
- [微软 WPF 每显示器 DPI 示例](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md)：沿用 WPF 原生能力。
- [WPF 旧版居中问题 #6103](https://github.com/dotnet/wpf/issues/6103)：作为坐标处理的历史参考，不将已关闭的旧版问题宣称为 .NET 10 当前缺陷。

## 候选验收记录

最终产品提交 `a96df6f835bfc6dbe8fe4b349d701463777220b2` 的 [最终 Windows CI 38401072088](https://github.com/Nomoziya/nomozip/actions/runs/38401072088) 两个作业全部成功，44 份报告下载至 `artifacts/github-validation-38401072088/`，与既有集合加新增 DPI 报告逐项核对全部通过。新增 DPI 97 项；既有 F24 96/20/exFAT 36、编码 75/31、密码整理 30/30、SHA-256 34/43、引擎 101、压缩 38、混合密码 48、A4 104/58、选择 86/31、备份 34/24/跨账号 5、软件更新 28/36、Shell 31/10/21、实际 Shell、现代菜单原型、安装升级 21、布局/焦点/显示 45/17/13 等保持通过。首轮 CI 38400426716 也成功；清理剩余三处主屏预缩小后按最终代码完整复验，没有放宽要求。

本地轻量候选 0.2.0-preview.10 载荷 9590705 字节 / 43 文件，比 F24 增加 38331 字节；安装器 2239713 字节。worker、官方 26.03/26.04 DLL、公钥及渠道配置逐字节未变，无新增运行组件。四项本地轻量资产位于 `artifacts/release-0.2.0-preview.10/`；载荷复核见 `artifacts/dpi-payload-validation.json`，CRC、SHA-256、源码一致性和私密材料排除见 `artifacts/dpi-package-validation.json`。未发布新正式版或替换实际安装。
