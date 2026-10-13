# 右键解压与压缩

此功能属于本地开发候选 0.2.0-preview.2；公开正式版 0.1.0 不包含此入口。基础菜单已实现，当前在等待最终云端验收。

## 使用

打开“设置 → 右键菜单 → 添加右键菜单”。这是当前 Windows 用户的可选入口，不修改默认应用，也不自动添加“打开方式”。Windows 11 请在右键的“显示更多选项”中找到 nomozip。

- 选择 7z、ZIP 或 RAR：可“解压到当前目录”或“解压到同名文件夹”，也可进入压缩页。
- 选择其它文件或文件夹：可进入“压缩…”；多个项目会一次传入同一窗口，先确认格式、输出和密码，再创建压缩包。
- 直接解压使用现有密码库、自动尝试、同名冲突与取消流程。多包分别输出到各包所在目录或各自同名文件夹；解压页显示首个目标并说明规则，任务提示可查看各自目标。“浏览目录”可改为统一输出目录。
- 改变压缩格式时，目标名的已知 7z/ZIP 扩展名随之更新；自定义其它扩展名仍须由用户确认。
- “移除右键菜单”只移除当前程序位置拥有的菜单。升级保留已启用菜单，卸载清理本安装的登记；其它程序位置的登记保持不变。移动 ZIP 版目录后，应从新位置重新添加入口。

菜单不会把解压密码放入启动参数，也不使用命令行拼接每个路径。源项目已移动、丢失或不受该动作支持时，整批拒绝，不悄悄过滤后误报成功。

## 实现与验收边界

基础菜单使用 Windows 静态级联菜单、`MultiSelectModel=Player` 与 [IExecuteCommand / IObjectWithSelection](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexecutecommand)。`ArchivePassword.exe --shell-server` 作为本地 COM 服务器，只在执行命令时启动；Explorer 不加载 .NET，无新增程序或运行组件。三种动作共享原有业务和官方引擎。登记采用 HKCU，不要求更改系统默认关联；参考[微软菜单登记与选择模型](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)和[静态级联菜单](https://learn.microsoft.com/en-us/windows/win32/shell/creating-static-cascading-menus)。

本地隔离登记 31 项通过；原生 COM → 实际 WPF 流程 10 项通过，含中文/空格/特殊字符、不同父目录、真实输出和 300 个长路径项目（合计超过命令行上限）；实际设置按钮、窄窗口/模拟高对比度和压缩创建 21 项通过；安装升级/卸载 21 项通过。报告为 `artifacts/shell-registration-validation.json`、`shell-native-validation.json`、`shell-ui-validation.json` 和 `light-installer-validation.json`。测试使用合成数据与隔离登记，云端另验证真实 Windows Shell 菜单枚举和委托执行。

## Windows 11 原生菜单技术原型

已编译 `src/ShellPrototype/ExplorerCommand.cpp`，通过 IExplorerCommand 子命令枚举、状态查询和向现有程序的真实委托；`AppxManifest.xml` 提供 native COM、现有 EXE、文件/目录菜单和 sparse identity 声明，已校验 CLSID 对应关系。原型 DLL 169984 字节，不随本次轻量产品分发。

[微软原生菜单说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)要求 IExplorerCommand 和包身份。当前未安装或验证该 sparse package，也未在真实 Windows 11 第一层菜单展示验收；没有把 XML 可读或接口通过当作部署成功。后续 B3 继续包身份、签名/部署、升级移除与真实 Windows 11 展示验收。本地未安装证书、启用开发者模式或重启 Explorer。
