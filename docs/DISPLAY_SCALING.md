# 每显示器 DPI 与小工作区

0.2.0-preview.10 开发候选采用 PerMonitorV2，沿用 WPF、现有字体和设计规范，不增加运行组件。公开正式版 0.1.0 不因此改变。

Windows 与 WPF 负责按窗口所在屏幕的 DPI 渲染；程序只根据当前显示器工作区调整窗口大小和位置。对话框优先围绕所有者居中，屏幕负坐标按原生像素处理。拖动跨屏期间不会将窗口夹回旧显示器；移动结束、DPI 或工作区变化后重新适配。最大化和最小化保留 Windows 原生行为。

主窗口与密码、密码编辑、备份、导入、解压规则、内容、失败详情、SHA-256、编码和结果目录十类对话框共用此行为。常规尺寸保留既有布局，极小工作区下内容和编码窗口收起重复说明；SHA-256 将所选文件的校验区变为可展开区域，放大后恢复完整布局，已输入值继续保留。

## 验证范围

`--validate-dpi` 使用独立合成数据和不可见测试窗口，核实 EXE 内嵌清单、真实 HWND 的 PMv2 上下文、WPF 与原生 DPI 一致、工作区边界、主窗四页和十类对话框操作可达。另验证 512×360 DIP 下列表整行和校验输入、正常布局恢复，以及 100%/125%/150%/200%/250% 与负屏幕坐标的几何计算。报告为 `artifacts/dpi-ui-validation.json`，截图为 `artifacts/dpi-preview/`。

本机实际测得 144 DPI（150%），只枚举一个活动显示器。几何/紧凑布局模拟不能证明真实不同 DPI 屏幕间移动、系统窗口重排或文字清晰度已通过；这些保持未实测。测试不改变用户的系统缩放、默认应用或真实密码库，不替换已安装程序。

## 原始依据

- [Windows 默认 DPI 感知清单](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)：使用 manifest 声明 PMv2，保留旧感知回退。
- [WPF OnDpiChanged](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.ondpichanged?view=windowsdesktop-10.0)：由框架接收并更新 DPI。
- [微软 WPF 每显示器 DPI 示例](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md)：沿用 WPF 原生能力。
- [WPF 旧版居中问题 #6103](https://github.com/dotnet/wpf/issues/6103)：作为坐标处理的历史参考，不将已关闭的旧版问题宣称为 .NET 10 当前缺陷。
