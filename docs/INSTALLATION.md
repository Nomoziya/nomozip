# 安装、升级与卸载

`nomozip-0.1.0-preview.3-setup-win-x64.exe` 为 Windows x64 当前用户安装包，已包含 .NET 10 / WPF 运行组件，不需要管理员权限或另装运行时。默认程序目录为 `%LOCALAPPDATA%/Programs/nomozip`，开始菜单提供程序和卸载入口。安装前关闭正在运行的 nomozip。

密码库与设置位于 `%LOCALAPPDATA%/ArchivePassword`，与程序目录分离。使用新版本安装包覆盖安装会保留密码库、设置和已下载的引擎。卸载默认保留这些数据，重新安装后可以继续使用。卸载向导只有在用户勾选删除选项并再次确认后才删除本机数据；静默卸载始终保留数据。程序目录中的非产品文件不由卸载器删除。

独立 ZIP 版也包含运行组件。它使用相同的 Windows 用户密码库；“独立”表示无需安装运行时，不表示密码库可以跨电脑复制使用。软件仍是开发预览，Windows 可执行文件暂未使用 Authenticode 证书签名。

维护者在已有引擎与 SDK 环境中构建：

```powershell
./scripts/bootstrap-installer.ps1
./scripts/build.ps1 -Configuration Release -SkipWorker -SelfContained -OutputDirectory artifacts/self-contained-app
./scripts/build-installer.ps1 -OutputFile artifacts/new-release/nomozip-setup-win-x64.exe
./scripts/test-installer.ps1
```

NSIS 编译器按 MSYS2 官方包 SHA-256 验证并解压至项目 `.tools`，不安装为全局工具。运行组件来自官方 NuGet。发布必须使用新的输出位置，脚本不覆盖已有安装包。更新 .NET 后须重新构建产品，不能依赖系统运行时自动更新这个包。

验收脚本实际执行两版安装、覆盖升级、安装目录中离线密码解压、默认保留数据的卸载、重装及明确删除虚构数据。安装路径、数据和快捷方式全部限定于 `artifacts/installer-validation/唯一目录`，使用独立 `nomozip-validation` 注册表项。删除路径在运行前核对，不能指定真实密码库。测试构建专用的删除参数不编译进公开安装程序。

该自动验收不代替安装向导、键盘、Narrator、高对比度和不同 DPI 的人工验收。
