# 安装、升级与卸载

提供两种 Windows x64 分发，功能相同：`nomozip-0.1.0-preview.7-light-setup-win-x64.exe` 为轻量安装版，程序目录约 9 MB，使用系统共享的 .NET 10 Windows Desktop Runtime x64（10.0.12 或更高的 10.0 补丁版）；`nomozip-0.1.0-preview.7-setup-win-x64.exe` 为完整独立版，携带运行组件，无需另装运行时。两者均为当前用户安装，默认程序目录为 `%LOCALAPPDATA%/Programs/nomozip`，开始菜单提供程序和卸载入口。安装前关闭 nomozip。

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

## 第七预览的人工验收

第四预览的压缩加密和自动解密，以及第五预览的解压布局和约 9 MB 体积，已获得用户确认，本轮保留这些结果。第七预览新增压缩包内容浏览，压缩页和密码库沿用第六预览的调整。优先确认浏览及两页布局，再补齐实际桌面操作。

下载 [第七预览轻量安装 EXE](https://github.com/Nomoziya/nomozip/releases/download/v0.1.0-preview.7/nomozip-0.1.0-preview.7-light-setup-win-x64.exe)，关闭 nomozip 后，选择现有安装器登记的程序目录进行升级。ZIP 覆盖安装不属于本次升级路径。启动后确认设置中的软件版本为 `0.1.0-preview.7`。

| 检查 | 操作和通过标准 | 当前人工状态 |
| --- | --- | --- |
| 轻量升级 | 查看程序目录属性，正常载荷约 9 MB；打开密码库和设置，原有记录仍在。程序目录内自行保存的文件或额外引擎会增加大小，不计入基础载荷。 | 待确认 |
| 解压布局与基础体积 | 用户已确认第五预览解压布局符合预期，安装目录约 9 MB。保留该结果。 | 第五预览人工通过 |
| 内容浏览 | 选择一个压缩包，点击“查看内容”或 Control L，搜索包内路径；文件名加密时可自动尝试密码库或手动输入。取消后可以继续正常解压；浏览不会写入输出目录或保存新密码。 | 第七预览待确认 |
| 压缩布局 | 宽窗口与解压页相同：顶部添加、左侧内容、右侧设置、固定底部操作；缩窄后设置在列表下方且可滚动到达。分别选择 7z/ZIP、展开加密设置，检查文字和操作可达。 | 第六预览待确认 |
| 密码库布局 | 添加多条测试记录，确认行距与对齐协调；搜索、选择、编辑和删除仍可用。名称/备注过长时显示省略与完整提示，启停和未使用状态清楚。 | 第六预览待确认 |
| 键盘 | 用 Tab/Shift+Tab、导航方向键、Enter、Escape 完成页面切换、选择文件和密码对话框操作；焦点清晰，所有主操作可到达。 | 待确认 |
| 讲述人 | 开启 Windows 讲述人，检查输入框、按钮、密码输入和错误提示的朗读是否清楚；完成后恢复原状态。 | 待确认 |
| 主题与缩放 | 记录原设置，在 Windows 对比度主题及可用的 125%/150%/200% 缩放下检查文字和按钮；有多个显示器时移动窗口，检查裁切及操作可达性，完成后恢复原设置。未具备的环境记为未测试。 | 待确认 |
| Explorer 入口 | 在软件设置中启用打开方式，从资源管理器用 nomozip 打开测试包；移除入口后，原默认应用仍保留。 | 待确认 |

按实际完成的项目反馈即可，未检查的项目保持待确认。例如：

```text
版本：preview.7 轻量版
升级：通过 / 问题；安装目录大小：___ MB；旧记录：保留 / 未保留
压缩布局 / 密码库布局：通过 / 问题；窗口大小和系统缩放：___
键盘 / 讲述人 / 主题与缩放 / Explorer：通过 / 问题 / 未测试
发现问题时：操作步骤 → 实际结果 → 预期结果
```
