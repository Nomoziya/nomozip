# 安装、升级与卸载

提供两种 Windows x64 分发，功能相同：`nomozip-0.1.0-preview.5-light-setup-win-x64.exe` 为轻量安装版，程序目录约 9 MB，使用系统共享的 .NET 10 Windows Desktop Runtime x64（10.0.12 或更高的 10.0 补丁版）；`nomozip-0.1.0-preview.5-setup-win-x64.exe` 为完整独立版，携带运行组件，无需另装运行时。两者均为当前用户安装，默认程序目录为 `%LOCALAPPDATA%/Programs/nomozip`，开始菜单提供程序和卸载入口。安装前关闭 nomozip。

轻量版未携带运行库，未安装时须从 [微软 .NET 10 下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0) 选择 **.NET Desktop Runtime / Windows / x64**，不需要 SDK。程序使用微软 apphost 的运行库解析和缺失提示。约 9 MB 是软件自身的占用，不包括系统共享运行库；首次安装运行库的整机新增空间不会等同于官方 7-Zip。现有机型无需为了轻量化更换 WPF 或删除功能。

密码库与设置位于 `%LOCALAPPDATA%/ArchivePassword`，与程序目录分离。使用新版本安装包覆盖安装会保留密码库、设置和已下载的引擎。卸载默认保留这些数据，重新安装后可以继续使用。卸载向导只有在用户勾选删除选项并再次确认后才删除本机数据；静默卸载始终保留数据。程序目录中的非产品文件不由卸载器删除。

完整 ZIP 版包含运行组件，文件名带 `-light-` 的 ZIP 使用共享运行库；ZIP 必须解压到新的空目录，不要直接覆盖完整旧版，否则旧 DLL 会残留。两者使用相同的 Windows 用户密码库；“完整独立”表示无需安装运行时，不表示密码库可以跨电脑复制使用。软件仍是开发预览，Windows 可执行文件暂未使用 Authenticode 证书签名。

从已登记的完整旧版升级到轻量版时，安装器先调用该安装自己的卸载清单清理程序载荷，再写入轻量载荷。仅在卸载注册记录的安装位置与当前目标一致时执行；保留数据和非产品文件。用户已经启用的打开方式登记在此过程中保持存在，避免旧卸载器清理它，随后立即恢复原命令。轻量安装不会卸载或清理系统共享的 .NET。

维护者在已有引擎与 SDK 环境中构建：

```powershell
./scripts/bootstrap-installer.ps1
./scripts/build.ps1 -Configuration Release -SkipWorker -SelfContained -OutputDirectory artifacts/self-contained-app
./scripts/build-installer.ps1 -OutputFile artifacts/new-release/nomozip-setup-win-x64.exe
./scripts/test-installer.ps1
./scripts/build.ps1 -Configuration Release -SkipWorker -OutputDirectory artifacts/light-app
./scripts/build-installer.ps1 -FrameworkDependent -OutputFile artifacts/new-release/nomozip-light-setup-win-x64.exe
./scripts/test-installer.ps1 -FrameworkDependent
```

NSIS 编译器按 MSYS2 官方包 SHA-256 验证并解压至项目 `.tools`，不安装为全局工具。完整版运行组件来自官方 NuGet。发布必须使用新的输出位置，脚本不覆盖已有安装包。完整版更新 .NET 后须重新构建；轻量版由系统共享运行库提供兼容的补丁更新。

验收脚本实际执行两版安装、覆盖升级、安装目录中离线密码解压、默认保留数据的卸载、重装及明确删除虚构数据。安装路径、数据和快捷方式全部限定于 `artifacts/installer-validation/唯一目录`，使用独立 `nomozip-validation` 注册表项。删除路径在运行前核对，不能指定真实密码库。测试构建专用的删除参数不编译进公开安装程序。

该自动验收不代替安装向导、键盘、Narrator、高对比度和不同 DPI 的人工验收。
