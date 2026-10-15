# 软件更新

开发候选 0.2.0-preview.26 在设置的“软件更新”卡片中提供检查、下载和安装重启。只分发共享 .NET 运行库的轻量版，分别匹配 x64 / 原生 ARM64。

1. 点击“检查软件更新”，才匿名访问产品的 GitHub Releases；不后台轮询、不登录。
2. 新正式版有匹配架构的轻量安装包时，可打开下载页查看说明。已登记的当前用户安装版还可点击“下载安装包”。
3. 下载显示进度，可以取消。实际字节数和 SHA-256 必须与 GitHub API 元数据一致；校验通过前不能安装。
4. 点击“安装并重启”并确认后，软件再次校验下载文件，启动安装器，再退出。安装器等待 GUI / CLI 释放程序文件，升级当前登记目录，成功后重新打开软件。密码库、设置、语言/外观偏好、下载的引擎沿用现有保留规则。

ZIP 或未登记的副本不能自动覆盖其它安装，继续使用下载页；ZIP 用户解压新版到空目录。检查失败清除上次提供的更新；下载失败、取消、校验不符或安装进程未能启动时，当前软件保持可用，允许重试。拒绝安装确认会保留已经准备好的安装包。

## 版本、来源与校验

- 只选择正式标签 `v主.次.补丁`，排除草稿、预览及引擎标签。同一核心版本的正式版可以替换预览，不提示降级。
- 精确匹配 `nomozip-{version}-light-setup-win-{architecture}.exe` 和产品仓库的固定资产 URL；不回退到其它架构或捆绑运行组件的版本。
- 使用 [GitHub Releases 列表接口](https://docs.github.com/en/rest/releases/releases#list-releases)，处理分页，不使用通常指向引擎的 `/releases/latest`。软件仓库固定为 `Nomoziya/nomozip`，设置中的引擎仓库不能改变软件来源。
- 自动下载要求有效的 `sha256:` digest、正数大小，且不超过 64 MiB。没有可用校验信息或超过限制时仍提供发行页面，不自动下载。
- [GitHub 发布资产 digest](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/) 是通过 GitHub HTTPS 元数据取得的下载完整性依据，**不是独立发行者签名**。Windows 商业代码签名已由用户移出范围；现有引擎 RSA 签名校验继续保留。
- 不完整下载删除本次 `.partial`。成功文件在当前用户资料的 `application-updates` 缓存内；重新检查会删除本会话准备的文件。实际安装交接后的安装包保留供故障修复，不在安装器运行时强行删除，可在不用时删除该缓存目录。

## 安装交接与失败边界

使用现有 NSIS 当前用户安装器，不要求管理员权限。启动参数为 `/S /UPDATE /D=当前安装目录`；按 [NSIS 命令行约定](https://nsis.sourceforge.io/Docs/Chapter3.html#installerusage)，`/D` 放在最后，其路径即使含空格也不额外加引号。可执行文件路径通过 `ProcessStartInfo.FileName` 独立传入，不使用命令 shell。

`/UPDATE` 必须与当前用户登记的安装目录一致，且已存在主程序。先等待 GUI / CLI 文件解锁，最多约 20 秒；超时或目录不符，在修改安装前终止并说明原因。退出前还会重新计算哈希，并在创建安装进程期间持有禁止写入/替换的只读文件句柄。其它 nomozip 实例或命令行任务应先结束。

安装成功自动打开新版本；重新打开失败时提示从开始菜单启动。升级中断沿用现有安装器的修复方式：重新运行缓存或下载的安装包。**尚未实现软件本体的事务回滚、断电恢复或任务自动续作**；不能把引擎版本回退描述成软件安装回滚。重启后不自动继续正在进行的任务，跨重启续作仍属于 F07。

## 验证

本期验证仍在进行，最终提交、双架构 CI 和候选资产的状态见 PROJECT_PLAN.md。

- `tests/ApplicationUpdateValidation`：原版本/架构/分页/来源判断，新增 digest、实际流大小、SHA-256、失败清理、下载中途取消、重试、不相关文件保留、安装前文件变更拒绝、只读锁和含空格/中文路径参数。
- `ArchivePassword.exe --validate-application-updates <项目目录> zh-CN|en`：实际 WPF / UIA，隔离 HTTP、启动与退出替身，验证可取消/重试、ZIP 降级入口、确认拒绝、进程失败不退出、资料字节不变及三种主题/两种尺寸。不会安装真实软件或操作实际密码库。
- `scripts/test-installer.ps1`：仅 CI 执行的隔离登记与安装，实际 `/UPDATE` 等待、超时保留、错目录拒绝、安装成功和重启后的真实离线解压；验证重启使用专用资料，不接触实际用户安装。
- 报告：`application-update-validation.json`、`application-update-ui-validation.json`、`application-update-en-ui-validation.json`、`light-installer-validation.json`。

实际桌面安装、可见系统通知、讲述人真实发声等物理环境项保留原状态；本轮不发布正式版本，不替换本机实际安装。
