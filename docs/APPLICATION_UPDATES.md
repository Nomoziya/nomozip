# 软件更新

开发候选 0.2.0-preview.3 在“设置”中提供独立的“软件更新”卡片。

1. 点击“检查软件更新”。此时才匿名连接产品的 GitHub Releases；没有后台定时请求，也不需要登录。
2. 如果发现较新的正式版，点击“打开软件下载页”，查看发行说明并下载对应 `win-x64` 的轻量安装包。
3. 关闭 nomozip，再运行安装器升级。密码库、设置和已下载的引擎按现有安装规则保留。ZIP 用户请解压到新的空目录。

首期提供版本提醒和下载页入口，不自动下载、安装或重启。未发布正式版的开发预览不会被当作更新；同版本号的正式版可替代预览版，较低正式版不会提示降级。不存在当前架构的轻量安装包时，不提供其它架构或带运行组件的安装包。ARM64/x86 的元数据匹配已用样本验证，不代表产品已支持这些架构。

断网、超时、GitHub 限流与取消会显示可读状态；失败不会保留此前检查的下载按钮。检查过程中暂停其它修改操作，可以取消检查。

## 与引擎更新的区别

“解压引擎”卡片仍使用签名包安装、启用和回退，地址栏只控制引擎仓库。软件版本始终从产品仓库 `Nomoziya/nomozip` 检查；改变引擎仓库不会改变软件下载来源。

软件检查使用 [GitHub Releases 列表接口](https://docs.github.com/en/rest/releases/releases#list-releases) 并处理分页，筛选正式软件标签、架构和轻量安装包。它不读取 `/releases/latest`；现有该入口继续用于 `engine-26.04`，不能把引擎版本当作软件更新。打开的页面固定在该产品仓库；软件发行签名是后续 F17 的范围，不能将引擎包签名描述为软件安装器签名。

## 验证

最终代码 `d381c9d` 的 [Windows CI 38342403339](https://github.com/Nomoziya/nomozip/actions/runs/38342403339) 全部通过，30 份报告含新增 28 项版本判断与 36 项界面检查，以及现有功能、实际 Shell、跨用户迁移与 21 项安装升级回归。无登录公开查询也已确认软件正式版 0.1.0，与 engine-26.04 的最新引擎入口分离。仅验证 x64 产品；其它架构仅为元数据样本。

- `tests/ApplicationUpdateValidation`：正式/预览/引擎/草稿筛选、数字版本比较、架构与轻量资产、分页、下载来源、HTTP 错误及取消；可增加 `--online` 对公开产品仓库只读检查。
- `ArchivePassword.exe --validate-application-updates <项目目录>`：使用独立合成资料、HTTP 夹具和浏览器替身，验证实际 WPF 绑定、UIA 调用、窄窗口、主题、错误状态与引擎独立性；不会读取实际用户库、开启真实浏览器或安装软件。
- 本地报告 `artifacts/application-update-validation.json`、`artifacts/application-update-ui-validation.json`、`artifacts/application-update-public-validation.json`。持续集成通过 `scripts/test.ps1` 执行前两项。
